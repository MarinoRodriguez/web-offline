using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WebOffline.Core.Common;
using WebOffline.Core.DTOs;
using WebOffline.Core.Entities;
using WebOffline.Core.Interfaces;

namespace WebOffline.Infrastructure.Services;

public class SyncService : ISyncService
{
    private readonly ISyncRepository _syncRepository;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IListRepository _listRepository;
    private readonly ITaskRepository _taskRepository;
    private readonly IAbacPolicyEvaluator _abacEvaluator;
    private readonly IAuditService _auditService;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SyncService(
        ISyncRepository syncRepository,
        IWorkspaceRepository workspaceRepository,
        IListRepository listRepository,
        ITaskRepository taskRepository,
        IAbacPolicyEvaluator abacEvaluator,
        IAuditService auditService)
    {
        _syncRepository = syncRepository;
        _workspaceRepository = workspaceRepository;
        _listRepository = listRepository;
        _taskRepository = taskRepository;
        _abacEvaluator = abacEvaluator;
        _auditService = auditService;
    }

    public async Task<ApiResponse<SyncBatchResponse>> SyncBatchAsync(
        SyncBatchRequest request,
        string currentUserId,
        string currentUserEmail,
        bool isSystemAdmin)
    {
        var response = new SyncBatchResponse
        {
            SyncedAt = DateTime.UtcNow
        };

        // 1. Process PUSH (mutations from client)
        if (request.Mutations != null && request.Mutations.Any())
        {
            foreach (var mutation in request.Mutations)
            {
                try
                {
                    var result = await ProcessMutationAsync(mutation, currentUserId, currentUserEmail, isSystemAdmin);
                    if (result.Success)
                    {
                        response.AppliedCount++;
                    }
                    else
                    {
                        response.Rejections.Add(new SyncRejectionDto
                        {
                            MutationId = mutation.Id,
                            Entity = mutation.Entity,
                            EntityId = mutation.EntityId,
                            Reason = result.Message ?? "Mutation could not be applied.",
                            StatusCode = result.StatusCode
                        });
                    }
                }
                catch (Exception ex)
                {
                    response.Rejections.Add(new SyncRejectionDto
                    {
                        MutationId = mutation.Id,
                        Entity = mutation.Entity,
                        EntityId = mutation.EntityId,
                        Reason = ex.Message,
                        StatusCode = 500
                    });
                }
            }
        }

        // 2. Process PULL (changes since LastSyncedAt)
        var pullResult = await FetchChangesAsync(request.LastSyncedAt, request.WorkspaceId, currentUserId, isSystemAdmin);
        response.Changes = pullResult;

        return ApiResponse<SyncBatchResponse>.Ok(response, "Sync batch executed successfully.");
    }

    public async Task<ApiResponse<SyncBatchResponse>> PullChangesAsync(
        DateTime? lastSyncedAt,
        string? workspaceId,
        string currentUserId,
        bool isSystemAdmin)
    {
        var response = new SyncBatchResponse
        {
            SyncedAt = DateTime.UtcNow,
            AppliedCount = 0
        };

        response.Changes = await FetchChangesAsync(lastSyncedAt, workspaceId, currentUserId, isSystemAdmin);
        return ApiResponse<SyncBatchResponse>.Ok(response, "Pull changes executed successfully.");
    }

    #region Mutation Processing

    private async Task<ApiResponse> ProcessMutationAsync(
        SyncMutationDto mutation,
        string currentUserId,
        string currentUserEmail,
        bool isSystemAdmin)
    {
        var entityType = mutation.Entity?.ToLowerInvariant();
        var operation = mutation.Operation?.ToUpperInvariant();

        return entityType switch
        {
            "workspace" => await ProcessWorkspaceMutationAsync(mutation, operation, currentUserId, currentUserEmail, isSystemAdmin),
            "list" => await ProcessListMutationAsync(mutation, operation, currentUserId, currentUserEmail, isSystemAdmin),
            "task" => await ProcessTaskMutationAsync(mutation, operation, currentUserId, currentUserEmail, isSystemAdmin),
            _ => ApiResponse.Fail($"Unsupported entity type: '{mutation.Entity}'.", 400)
        };
    }

    private async Task<ApiResponse> ProcessWorkspaceMutationAsync(
        SyncMutationDto mutation,
        string? operation,
        string currentUserId,
        string currentUserEmail,
        bool isSystemAdmin)
    {
        var payload = DeserializePayload(mutation.Payload);

        switch (operation)
        {
            case "INSERT":
            {
                var existing = await _workspaceRepository.GetByIdAsync(mutation.EntityId);
                if (existing != null)
                {
                    return ApiResponse.Fail("Workspace already exists on server.", 409);
                }

                var wsName = payload?.Name ?? "New Workspace";
                var ws = new Workspace
                {
                    Id = mutation.EntityId,
                    Name = wsName.Trim(),
                    Description = payload?.Description?.Trim(),
                    OwnerId = currentUserId,
                    CreatedBy = currentUserId,
                    CreatedAt = mutation.ClientTimestamp,
                    UpdatedBy = currentUserId,
                    UpdatedAt = mutation.ClientTimestamp,
                    Version = mutation.Version > 0 ? mutation.Version : 1,
                    IsDeleted = false
                };

                await _workspaceRepository.CreateAsync(ws);

                await _auditService.RecordChangeAsync(
                    currentUserId,
                    currentUserEmail,
                    "SYNC_CREATE",
                    "Workspace",
                    ws.Id,
                    $"{currentUserEmail} creó el workspace '{ws.Name}' vía sync",
                    (Workspace?)null,
                    ws);

                return ApiResponse.Ok();
            }

            case "UPDATE":
            {
                var canWrite = await _abacEvaluator.CanAccessWorkspaceAsync(currentUserId, mutation.EntityId, ResourceAction.Write, isSystemAdmin);
                if (!canWrite)
                {
                    return ApiResponse.Fail("Access denied to update workspace.", 403);
                }

                var existing = await _workspaceRepository.GetByIdAsync(mutation.EntityId);
                if (existing == null)
                {
                    return ApiResponse.Fail("Workspace not found.", 404);
                }

                // Conflict check (Last-Write-Wins)
                if (existing.UpdatedAt.ToUniversalTime() > mutation.ClientTimestamp.ToUniversalTime())
                {
                    return ApiResponse.Fail("Conflict: Server has a more recent version of this workspace.", 409);
                }

                var oldState = CloneWorkspace(existing);
                if (!string.IsNullOrWhiteSpace(payload?.Name)) existing.Name = payload.Name.Trim();
                if (payload?.Description != null) existing.Description = payload.Description.Trim();
                existing.UpdatedBy = currentUserId;
                existing.UpdatedAt = DateTime.UtcNow;
                existing.Version = Math.Max(existing.Version + 1, mutation.Version);

                await _workspaceRepository.UpdateAsync(existing);

                await _auditService.RecordChangeAsync(
                    currentUserId,
                    currentUserEmail,
                    "SYNC_UPDATE",
                    "Workspace",
                    existing.Id,
                    $"{currentUserEmail} actualizó el workspace '{existing.Name}' vía sync",
                    oldState,
                    existing);

                return ApiResponse.Ok();
            }

            case "DELETE":
            {
                var canDelete = await _abacEvaluator.CanAccessWorkspaceAsync(currentUserId, mutation.EntityId, ResourceAction.Delete, isSystemAdmin);
                if (!canDelete)
                {
                    return ApiResponse.Fail("Access denied to delete workspace.", 403);
                }

                var existing = await _workspaceRepository.GetByIdAsync(mutation.EntityId);
                if (existing == null)
                {
                    return ApiResponse.Ok(); // Already deleted
                }

                await _workspaceRepository.SoftDeleteAsync(mutation.EntityId, currentUserId);
                await _syncRepository.RecordTombstoneAsync(new SyncTombstone
                {
                    Id = Guid.NewGuid().ToString(),
                    EntityType = "workspace",
                    EntityId = mutation.EntityId,
                    WorkspaceId = mutation.EntityId,
                    DeletedBy = currentUserId,
                    DeletedAt = DateTime.UtcNow
                });

                await _auditService.RecordChangeAsync(
                    currentUserId,
                    currentUserEmail,
                    "SYNC_DELETE",
                    "Workspace",
                    mutation.EntityId,
                    $"{currentUserEmail} eliminó el workspace '{existing.Name}' vía sync",
                    existing,
                    (Workspace?)null);

                return ApiResponse.Ok();
            }

            default:
                return ApiResponse.Fail($"Unsupported operation: '{operation}'.", 400);
        }
    }

    private async Task<ApiResponse> ProcessListMutationAsync(
        SyncMutationDto mutation,
        string? operation,
        string currentUserId,
        string currentUserEmail,
        bool isSystemAdmin)
    {
        var payload = DeserializePayload(mutation.Payload);
        var targetWorkspaceId = mutation.WorkspaceId ?? payload?.WorkspaceId;

        switch (operation)
        {
            case "INSERT":
            {
                if (string.IsNullOrWhiteSpace(targetWorkspaceId))
                {
                    return ApiResponse.Fail("WorkspaceId is required for List insertion.", 400);
                }

                var canWrite = await _abacEvaluator.CanAccessWorkspaceAsync(currentUserId, targetWorkspaceId, ResourceAction.Write, isSystemAdmin);
                if (!canWrite)
                {
                    return ApiResponse.Fail("Access denied to create list in this workspace.", 403);
                }

                var existing = await _listRepository.GetByIdAsync(mutation.EntityId);
                if (existing != null)
                {
                    return ApiResponse.Fail("List already exists on server.", 409);
                }

                var listName = payload?.Name ?? "New List";
                var list = new TaskList
                {
                    Id = mutation.EntityId,
                    WorkspaceId = targetWorkspaceId,
                    Name = listName.Trim(),
                    Color = string.IsNullOrWhiteSpace(payload?.Color) ? "#3B82F6" : payload.Color,
                    Position = payload?.Position ?? 0,
                    CreatedBy = currentUserId,
                    CreatedAt = mutation.ClientTimestamp,
                    UpdatedBy = currentUserId,
                    UpdatedAt = mutation.ClientTimestamp,
                    Version = mutation.Version > 0 ? mutation.Version : 1,
                    IsDeleted = false
                };

                await _listRepository.CreateAsync(list);

                await _auditService.RecordChangeAsync(
                    currentUserId,
                    currentUserEmail,
                    "SYNC_CREATE",
                    "List",
                    list.Id,
                    $"{currentUserEmail} creó la lista '{list.Name}' vía sync",
                    (TaskList?)null,
                    list);

                return ApiResponse.Ok();
            }

            case "UPDATE":
            {
                var existing = await _listRepository.GetByIdAsync(mutation.EntityId);
                if (existing == null)
                {
                    return ApiResponse.Fail("List not found.", 404);
                }

                var canWrite = await _abacEvaluator.CanAccessWorkspaceAsync(currentUserId, existing.WorkspaceId, ResourceAction.Write, isSystemAdmin);
                if (!canWrite)
                {
                    return ApiResponse.Fail("Access denied to update list.", 403);
                }

                // Conflict check
                if (existing.UpdatedAt.ToUniversalTime() > mutation.ClientTimestamp.ToUniversalTime())
                {
                    return ApiResponse.Fail("Conflict: Server has a more recent version of this list.", 409);
                }

                var oldState = CloneList(existing);
                if (!string.IsNullOrWhiteSpace(payload?.Name)) existing.Name = payload.Name.Trim();
                if (!string.IsNullOrWhiteSpace(payload?.Color)) existing.Color = payload.Color;
                if (payload?.Position.HasValue == true) existing.Position = payload.Position.Value;
                existing.UpdatedBy = currentUserId;
                existing.UpdatedAt = DateTime.UtcNow;
                existing.Version = Math.Max(existing.Version + 1, mutation.Version);

                await _listRepository.UpdateAsync(existing);

                await _auditService.RecordChangeAsync(
                    currentUserId,
                    currentUserEmail,
                    "SYNC_UPDATE",
                    "List",
                    existing.Id,
                    $"{currentUserEmail} actualizó la lista '{existing.Name}' vía sync",
                    oldState,
                    existing);

                return ApiResponse.Ok();
            }

            case "DELETE":
            {
                var existing = await _listRepository.GetByIdAsync(mutation.EntityId);
                if (existing == null)
                {
                    return ApiResponse.Ok();
                }

                var canWrite = await _abacEvaluator.CanAccessWorkspaceAsync(currentUserId, existing.WorkspaceId, ResourceAction.Write, isSystemAdmin);
                if (!canWrite)
                {
                    return ApiResponse.Fail("Access denied to delete list.", 403);
                }

                await _listRepository.SoftDeleteAsync(mutation.EntityId, currentUserId);
                await _syncRepository.RecordTombstoneAsync(new SyncTombstone
                {
                    Id = Guid.NewGuid().ToString(),
                    EntityType = "list",
                    EntityId = mutation.EntityId,
                    WorkspaceId = existing.WorkspaceId,
                    DeletedBy = currentUserId,
                    DeletedAt = DateTime.UtcNow
                });

                await _auditService.RecordChangeAsync(
                    currentUserId,
                    currentUserEmail,
                    "SYNC_DELETE",
                    "List",
                    mutation.EntityId,
                    $"{currentUserEmail} eliminó la lista '{existing.Name}' vía sync",
                    existing,
                    (TaskList?)null);

                return ApiResponse.Ok();
            }

            default:
                return ApiResponse.Fail($"Unsupported operation: '{operation}'.", 400);
        }
    }

    private async Task<ApiResponse> ProcessTaskMutationAsync(
        SyncMutationDto mutation,
        string? operation,
        string currentUserId,
        string currentUserEmail,
        bool isSystemAdmin)
    {
        var payload = DeserializePayload(mutation.Payload);

        switch (operation)
        {
            case "INSERT":
            {
                var listId = string.IsNullOrWhiteSpace(payload?.ListId) ? null : payload.ListId.Trim();
                string workspaceId;

                if (listId != null)
                {
                    var list = await _listRepository.GetByIdAsync(listId);
                    if (list == null)
                    {
                        return ApiResponse.Fail("Parent list not found on server.", 404);
                    }
                    workspaceId = mutation.WorkspaceId ?? payload?.WorkspaceId ?? list.WorkspaceId;
                }
                else
                {
                    workspaceId = mutation.WorkspaceId ?? payload?.WorkspaceId ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(workspaceId))
                    {
                        return ApiResponse.Fail("WorkspaceId is required for Task insertion when no ListId is specified.", 400);
                    }
                }

                var canWrite = await _abacEvaluator.CanAccessWorkspaceAsync(currentUserId, workspaceId, ResourceAction.Write, isSystemAdmin);
                if (!canWrite)
                {
                    return ApiResponse.Fail("Access denied to create tasks in this workspace.", 403);
                }

                var existing = await _taskRepository.GetByIdAsync(mutation.EntityId);
                if (existing != null)
                {
                    return ApiResponse.Fail("Task already exists on server.", 409);
                }

                var taskTitle = payload?.Title ?? "New Task";
                var task = new TaskItem
                {
                    Id = mutation.EntityId,
                    ListId = listId,
                    WorkspaceId = workspaceId,
                    ParentTaskId = payload?.ParentTaskId,
                    Title = taskTitle.Trim(),
                    Description = payload?.Description?.Trim(),
                    Status = payload?.Status?.ToUpperInvariant() ?? "TODO",
                    Priority = payload?.Priority?.ToUpperInvariant() ?? "MEDIUM",
                    DueDate = payload?.DueDate,
                    Position = payload?.Position ?? 0,
                    CreatedBy = currentUserId,
                    CreatedAt = mutation.ClientTimestamp,
                    UpdatedBy = currentUserId,
                    UpdatedAt = mutation.ClientTimestamp,
                    Version = mutation.Version > 0 ? mutation.Version : 1,
                    IsDeleted = false
                };

                await _taskRepository.CreateAsync(task);

                await _auditService.RecordChangeAsync(
                    currentUserId,
                    currentUserEmail,
                    "SYNC_CREATE",
                    "Task",
                    task.Id,
                    $"{currentUserEmail} creó la tarea '{task.Title}' vía sync",
                    (TaskItem?)null,
                    task);

                return ApiResponse.Ok();
            }

            case "UPDATE":
            {
                var existing = await _taskRepository.GetByIdAsync(mutation.EntityId);
                if (existing == null)
                {
                    return ApiResponse.Fail("Task not found.", 404);
                }

                var canWrite = await _abacEvaluator.CanAccessWorkspaceAsync(currentUserId, existing.WorkspaceId, ResourceAction.Write, isSystemAdmin);
                if (!canWrite)
                {
                    return ApiResponse.Fail("Access denied to update task.", 403);
                }

                // Business Rule: Subtask completion
                var targetStatus = payload?.Status?.ToUpperInvariant() ?? existing.Status;
                if (targetStatus == "DONE" && existing.Status != "DONE")
                {
                    var allCompleted = await _taskRepository.AreAllSubtasksCompletedAsync(mutation.EntityId);
                    if (!allCompleted)
                    {
                        var pendingCount = await _taskRepository.CountPendingSubtasksAsync(mutation.EntityId);
                        return ApiResponse.Fail(
                            $"Cannot complete task: There are {pendingCount} incomplete subtasks that must be completed first.", 400);
                    }
                }

                // Conflict check
                if (existing.UpdatedAt.ToUniversalTime() > mutation.ClientTimestamp.ToUniversalTime())
                {
                    return ApiResponse.Fail("Conflict: Server has a more recent version of this task.", 409);
                }

                var oldState = CloneTask(existing);
                if (!string.IsNullOrWhiteSpace(payload?.Title)) existing.Title = payload.Title.Trim();
                if (payload?.Description != null) existing.Description = payload.Description.Trim();
                if (!string.IsNullOrWhiteSpace(payload?.Status)) existing.Status = targetStatus;
                if (!string.IsNullOrWhiteSpace(payload?.Priority)) existing.Priority = payload.Priority.ToUpperInvariant();
                if (payload?.DueDate.HasValue == true) existing.DueDate = payload.DueDate;
                if (payload?.Position.HasValue == true) existing.Position = payload.Position.Value;
                if (payload != null && payload.ListId != null) existing.ListId = string.IsNullOrWhiteSpace(payload.ListId) ? null : payload.ListId.Trim();

                existing.UpdatedBy = currentUserId;
                existing.UpdatedAt = DateTime.UtcNow;
                existing.Version = Math.Max(existing.Version + 1, mutation.Version);

                await _taskRepository.UpdateAsync(existing);

                await _auditService.RecordChangeAsync(
                    currentUserId,
                    currentUserEmail,
                    "SYNC_UPDATE",
                    "Task",
                    existing.Id,
                    $"{currentUserEmail} actualizó la tarea '{existing.Title}' vía sync",
                    oldState,
                    existing);

                return ApiResponse.Ok();
            }

            case "DELETE":
            {
                var existing = await _taskRepository.GetByIdAsync(mutation.EntityId);
                if (existing == null)
                {
                    return ApiResponse.Ok();
                }

                var canWrite = await _abacEvaluator.CanAccessWorkspaceAsync(currentUserId, existing.WorkspaceId, ResourceAction.Write, isSystemAdmin);
                if (!canWrite)
                {
                    return ApiResponse.Fail("Access denied to delete task.", 403);
                }

                await _taskRepository.SoftDeleteAsync(mutation.EntityId, currentUserId);
                await _syncRepository.RecordTombstoneAsync(new SyncTombstone
                {
                    Id = Guid.NewGuid().ToString(),
                    EntityType = "task",
                    EntityId = mutation.EntityId,
                    WorkspaceId = existing.WorkspaceId,
                    DeletedBy = currentUserId,
                    DeletedAt = DateTime.UtcNow
                });

                await _auditService.RecordChangeAsync(
                    currentUserId,
                    currentUserEmail,
                    "SYNC_DELETE",
                    "Task",
                    mutation.EntityId,
                    $"{currentUserEmail} eliminó la tarea '{existing.Title}' vía sync",
                    existing,
                    (TaskItem?)null);

                return ApiResponse.Ok();
            }

            default:
                return ApiResponse.Fail($"Unsupported operation: '{operation}'.", 400);
        }
    }

    #endregion

    #region Pull Queries

    private async Task<SyncChangesDto> FetchChangesAsync(
        DateTime? lastSyncedAt,
        string? targetWorkspaceId,
        string currentUserId,
        bool isSystemAdmin)
    {
        var accessibleIds = (await _syncRepository.GetAccessibleWorkspaceIdsAsync(currentUserId, isSystemAdmin)).ToList();

        if (!string.IsNullOrWhiteSpace(targetWorkspaceId))
        {
            accessibleIds = accessibleIds.Where(id => id == targetWorkspaceId).ToList();
        }

        if (!accessibleIds.Any())
        {
            return new SyncChangesDto();
        }

        var workspaces = (await _syncRepository.GetWorkspacesUpdatedSinceAsync(lastSyncedAt, accessibleIds)).ToList();
        var lists = (await _syncRepository.GetListsUpdatedSinceAsync(lastSyncedAt, accessibleIds)).ToList();
        var tasks = (await _syncRepository.GetTasksUpdatedSinceAsync(lastSyncedAt, accessibleIds)).ToList();
        var tombstones = (await _syncRepository.GetTombstonesSinceAsync(lastSyncedAt, accessibleIds)).ToList();

        var wsDtos = workspaces.Select(w => new WorkspaceDto
        {
            Id = w.Id,
            Name = w.Name,
            Description = w.Description,
            OwnerId = w.OwnerId,
            RoleInWorkspace = w.OwnerId == currentUserId ? "Owner" : "Editor",
            CreatedBy = w.CreatedBy,
            CreatedAt = w.CreatedAt,
            UpdatedBy = w.UpdatedBy,
            UpdatedAt = w.UpdatedAt,
            Version = w.Version,
            IsDeleted = w.IsDeleted
        }).ToList();

        var listDtos = lists.Select(l => new ListDto
        {
            Id = l.Id,
            WorkspaceId = l.WorkspaceId,
            Name = l.Name,
            Color = l.Color,
            Position = l.Position,
            CreatedBy = l.CreatedBy,
            CreatedAt = l.CreatedAt,
            UpdatedBy = l.UpdatedBy,
            UpdatedAt = l.UpdatedAt,
            Version = l.Version,
            IsDeleted = l.IsDeleted
        }).ToList();

        var taskDtos = tasks.Select(t => new TaskDto
        {
            Id = t.Id,
            ListId = t.ListId,
            WorkspaceId = t.WorkspaceId,
            ParentTaskId = t.ParentTaskId,
            Title = t.Title,
            Description = t.Description,
            Status = t.Status,
            Priority = t.Priority,
            DueDate = t.DueDate,
            Position = t.Position,
            CreatedBy = t.CreatedBy,
            CreatedAt = t.CreatedAt,
            UpdatedBy = t.UpdatedBy,
            UpdatedAt = t.UpdatedAt,
            Version = t.Version,
            IsDeleted = t.IsDeleted
        }).ToList();

        var tombstoneDtos = tombstones.Select(t => new SyncTombstoneDto
        {
            EntityType = t.EntityType,
            EntityId = t.EntityId,
            WorkspaceId = t.WorkspaceId,
            DeletedAt = t.DeletedAt
        }).ToList();

        return new SyncChangesDto
        {
            Workspaces = wsDtos,
            Lists = listDtos,
            Tasks = taskDtos,
            Tombstones = tombstoneDtos
        };
    }

    #endregion

    #region Helper Models and Methods

    private class SyncEntityPayload
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? WorkspaceId { get; set; }
        public string? ListId { get; set; }
        public string? ParentTaskId { get; set; }
        public string? Title { get; set; }
        public string? Status { get; set; }
        public string? Priority { get; set; }
        public DateTime? DueDate { get; set; }
        public string? Color { get; set; }
        public int? Position { get; set; }
    }

    private static SyncEntityPayload? DeserializePayload(JsonElement? element)
    {
        if (!element.HasValue) return null;
        try
        {
            return JsonSerializer.Deserialize<SyncEntityPayload>(element.Value.GetRawText(), _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static Workspace CloneWorkspace(Workspace ws) => new()
    {
        Id = ws.Id,
        Name = ws.Name,
        Description = ws.Description,
        OwnerId = ws.OwnerId,
        CreatedBy = ws.CreatedBy,
        CreatedAt = ws.CreatedAt,
        UpdatedBy = ws.UpdatedBy,
        UpdatedAt = ws.UpdatedAt,
        Version = ws.Version,
        IsDeleted = ws.IsDeleted
    };

    private static TaskList CloneList(TaskList list) => new()
    {
        Id = list.Id,
        WorkspaceId = list.WorkspaceId,
        Name = list.Name,
        Color = list.Color,
        Position = list.Position,
        CreatedBy = list.CreatedBy,
        CreatedAt = list.CreatedAt,
        UpdatedBy = list.UpdatedBy,
        UpdatedAt = list.UpdatedAt,
        Version = list.Version,
        IsDeleted = list.IsDeleted
    };

    private static TaskItem CloneTask(TaskItem task) => new()
    {
        Id = task.Id,
        ListId = task.ListId,
        WorkspaceId = task.WorkspaceId,
        ParentTaskId = task.ParentTaskId,
        Title = task.Title,
        Description = task.Description,
        Status = task.Status,
        Priority = task.Priority,
        DueDate = task.DueDate,
        Position = task.Position,
        CreatedBy = task.CreatedBy,
        CreatedAt = task.CreatedAt,
        UpdatedBy = task.UpdatedBy,
        UpdatedAt = task.UpdatedAt,
        Version = task.Version,
        IsDeleted = task.IsDeleted
    };

    #endregion
}
