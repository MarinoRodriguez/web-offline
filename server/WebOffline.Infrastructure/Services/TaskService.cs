using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WebOffline.Core.Common;
using WebOffline.Core.DTOs;
using WebOffline.Core.Entities;
using WebOffline.Core.Interfaces;

namespace WebOffline.Infrastructure.Services;

public class TaskService : ITaskService
{
    private readonly ITaskRepository _taskRepository;
    private readonly IListRepository _listRepository;
    private readonly IAbacPolicyEvaluator _abacEvaluator;
    private readonly IAuditService _auditService;

    public TaskService(
        ITaskRepository taskRepository,
        IListRepository listRepository,
        IAbacPolicyEvaluator abacEvaluator,
        IAuditService auditService)
    {
        _taskRepository = taskRepository;
        _listRepository = listRepository;
        _abacEvaluator = abacEvaluator;
        _auditService = auditService;
    }

    public async Task<ApiResponse<IEnumerable<TaskDto>>> GetTasksByWorkspaceAsync(string workspaceId, string currentUserId, bool isSystemAdmin)
    {
        var canRead = await _abacEvaluator.CanAccessWorkspaceAsync(currentUserId, workspaceId, ResourceAction.Read, isSystemAdmin);
        if (!canRead)
        {
            return ApiResponse<IEnumerable<TaskDto>>.Fail("Access denied.", 403);
        }

        var allTasks = (await _taskRepository.GetByWorkspaceIdAsync(workspaceId)).ToList();
        var dtos = BuildTaskHierarchy(allTasks);

        return ApiResponse<IEnumerable<TaskDto>>.Ok(dtos);
    }

    public async Task<ApiResponse<IEnumerable<TaskDto>>> GetTasksByListAsync(string listId, string currentUserId, bool isSystemAdmin)
    {
        var canRead = await _abacEvaluator.CanAccessListAsync(currentUserId, listId, ResourceAction.Read, isSystemAdmin);
        if (!canRead)
        {
            return ApiResponse<IEnumerable<TaskDto>>.Fail("Access denied.", 403);
        }

        var tasks = (await _taskRepository.GetByListIdAsync(listId)).ToList();
        var dtos = BuildTaskHierarchy(tasks);

        return ApiResponse<IEnumerable<TaskDto>>.Ok(dtos);
    }

    public async Task<ApiResponse<TaskDto>> GetTaskByIdAsync(string taskId, string currentUserId, bool isSystemAdmin)
    {
        var task = await _taskRepository.GetByIdAsync(taskId);
        if (task == null)
        {
            return ApiResponse<TaskDto>.Fail("Task not found.", 404);
        }

        var canRead = await _abacEvaluator.CanAccessWorkspaceAsync(currentUserId, task.WorkspaceId, ResourceAction.Read, isSystemAdmin);
        if (!canRead)
        {
            return ApiResponse<TaskDto>.Fail("Access denied.", 403);
        }

        var subtasks = (await _taskRepository.GetSubtasksAsync(taskId)).ToList();
        var dto = MapToDto(task, subtasks);

        return ApiResponse<TaskDto>.Ok(dto);
    }

    public async Task<ApiResponse<TaskDto>> CreateTaskAsync(CreateTaskRequest request, string currentUserId, string currentUserEmail, bool isSystemAdmin)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.ListId))
        {
            return ApiResponse<TaskDto>.Fail("Title and ListId are required.", 400);
        }

        var list = await _listRepository.GetByIdAsync(request.ListId);
        if (list == null)
        {
            return ApiResponse<TaskDto>.Fail("Parent list not found.", 404);
        }

        var workspaceId = string.IsNullOrWhiteSpace(request.WorkspaceId) ? list.WorkspaceId : request.WorkspaceId;

        var canWrite = await _abacEvaluator.CanAccessWorkspaceAsync(currentUserId, workspaceId, ResourceAction.Write, isSystemAdmin);
        if (!canWrite)
        {
            return ApiResponse<TaskDto>.Fail("Access denied to create tasks in this workspace.", 403);
        }

        if (!string.IsNullOrWhiteSpace(request.ParentTaskId))
        {
            var parentTask = await _taskRepository.GetByIdAsync(request.ParentTaskId);
            if (parentTask == null)
            {
                return ApiResponse<TaskDto>.Fail("Parent task not found.", 404);
            }
        }

        var task = new TaskItem
        {
            Id = Guid.NewGuid().ToString(),
            ListId = request.ListId,
            WorkspaceId = workspaceId,
            ParentTaskId = request.ParentTaskId,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            Status = "TODO",
            Priority = string.IsNullOrWhiteSpace(request.Priority) ? "MEDIUM" : request.Priority.ToUpperInvariant(),
            DueDate = request.DueDate,
            Position = request.Position,
            CreatedBy = currentUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedBy = currentUserId,
            UpdatedAt = DateTime.UtcNow,
            Version = 1,
            IsDeleted = false
        };

        await _taskRepository.CreateAsync(task);

        // Audit changelog
        var actionLabel = string.IsNullOrWhiteSpace(task.ParentTaskId) ? "tarea" : "subtarea";
        await _auditService.RecordChangeAsync(
            currentUserId,
            currentUserEmail,
            "CREATE",
            "Task",
            task.Id,
            $"{currentUserEmail} creó la {actionLabel} '{task.Title}'",
            (TaskItem?)null,
            task);

        var dto = MapToDto(task, new List<TaskItem>());
        return ApiResponse<TaskDto>.Ok(dto, "Task created successfully", 201);
    }

    public async Task<ApiResponse<TaskDto>> UpdateTaskAsync(string taskId, UpdateTaskRequest request, string currentUserId, string currentUserEmail, bool isSystemAdmin)
    {
        var task = await _taskRepository.GetByIdAsync(taskId);
        if (task == null)
        {
            return ApiResponse<TaskDto>.Fail("Task not found.", 404);
        }

        var canWrite = await _abacEvaluator.CanAccessWorkspaceAsync(currentUserId, task.WorkspaceId, ResourceAction.Write, isSystemAdmin);
        if (!canWrite)
        {
            return ApiResponse<TaskDto>.Fail("Access denied.", 403);
        }

        var targetStatus = request.Status?.ToUpperInvariant() ?? task.Status;

        // Business Rule: If completing parent task, all subtasks must be DONE
        if (targetStatus == "DONE" && task.Status != "DONE")
        {
            var allCompleted = await _taskRepository.AreAllSubtasksCompletedAsync(taskId);
            if (!allCompleted)
            {
                var pendingCount = await _taskRepository.CountPendingSubtasksAsync(taskId);
                return ApiResponse<TaskDto>.Fail(
                    $"Cannot complete task: There are {pendingCount} incomplete subtasks that must be completed first.", 400);
            }
        }

        var oldState = new TaskItem
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

        task.Title = request.Title.Trim();
        task.Description = request.Description?.Trim();
        task.Status = targetStatus;
        task.Priority = request.Priority?.ToUpperInvariant() ?? task.Priority;
        task.DueDate = request.DueDate;
        task.Position = request.Position;
        task.UpdatedBy = currentUserId;
        task.UpdatedAt = DateTime.UtcNow;

        await _taskRepository.UpdateAsync(task);

        // Audit changelog
        await _auditService.RecordChangeAsync(
            currentUserId,
            currentUserEmail,
            "UPDATE",
            "Task",
            task.Id,
            $"{currentUserEmail} modificó la tarea '{task.Title}'",
            oldState,
            task);

        var subtasks = (await _taskRepository.GetSubtasksAsync(taskId)).ToList();
        var dto = MapToDto(task, subtasks);

        return ApiResponse<TaskDto>.Ok(dto, "Task updated successfully");
    }

    public async Task<ApiResponse<TaskDto>> UpdateTaskStatusAsync(string taskId, UpdateTaskStatusRequest request, string currentUserId, string currentUserEmail, bool isSystemAdmin)
    {
        var task = await _taskRepository.GetByIdAsync(taskId);
        if (task == null)
        {
            return ApiResponse<TaskDto>.Fail("Task not found.", 404);
        }

        var canWrite = await _abacEvaluator.CanAccessWorkspaceAsync(currentUserId, task.WorkspaceId, ResourceAction.Write, isSystemAdmin);
        if (!canWrite)
        {
            return ApiResponse<TaskDto>.Fail("Access denied.", 403);
        }

        var targetStatus = request.Status?.ToUpperInvariant() ?? "TODO";

        // Business Rule: If completing parent task, all subtasks must be DONE
        if (targetStatus == "DONE" && task.Status != "DONE")
        {
            var allCompleted = await _taskRepository.AreAllSubtasksCompletedAsync(taskId);
            if (!allCompleted)
            {
                var pendingCount = await _taskRepository.CountPendingSubtasksAsync(taskId);
                return ApiResponse<TaskDto>.Fail(
                    $"Cannot complete task: There are {pendingCount} incomplete subtasks that must be completed first.", 400);
            }
        }

        var oldState = new TaskItem
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

        var newVersion = task.Version + 1;
        await _taskRepository.UpdateStatusAsync(taskId, targetStatus, newVersion, currentUserId);

        task.Status = targetStatus;
        task.Version = newVersion;
        task.UpdatedBy = currentUserId;
        task.UpdatedAt = DateTime.UtcNow;

        // Audit changelog
        await _auditService.RecordChangeAsync(
            currentUserId,
            currentUserEmail,
            "STATUS_CHANGE",
            "Task",
            task.Id,
            $"{currentUserEmail} cambió el estado de la tarea '{task.Title}' a '{targetStatus}'",
            oldState,
            task);

        var subtasks = (await _taskRepository.GetSubtasksAsync(taskId)).ToList();
        var dto = MapToDto(task, subtasks);

        return ApiResponse<TaskDto>.Ok(dto, "Status updated successfully");
    }

    public async Task<ApiResponse> DeleteTaskAsync(string taskId, string currentUserId, string currentUserEmail, bool isSystemAdmin)
    {
        var task = await _taskRepository.GetByIdAsync(taskId);
        if (task == null)
        {
            return ApiResponse.Fail("Task not found.", 404);
        }

        var canWrite = await _abacEvaluator.CanAccessWorkspaceAsync(currentUserId, task.WorkspaceId, ResourceAction.Write, isSystemAdmin);
        if (!canWrite)
        {
            return ApiResponse.Fail("Access denied.", 403);
        }

        await _taskRepository.SoftDeleteAsync(taskId, currentUserId);

        // Audit changelog
        await _auditService.RecordChangeAsync(
            currentUserId,
            currentUserEmail,
            "DELETE",
            "Task",
            taskId,
            $"{currentUserEmail} eliminó la tarea '{task.Title}'",
            task,
            (TaskItem?)null);

        return ApiResponse.Ok("Task deleted successfully");
    }

    #region Helper Methods

    private static List<TaskDto> BuildTaskHierarchy(List<TaskItem> allTasks)
    {
        var topLevel = allTasks.Where(t => string.IsNullOrWhiteSpace(t.ParentTaskId)).ToList();
        var childrenLookup = allTasks.Where(t => !string.IsNullOrWhiteSpace(t.ParentTaskId))
                                     .GroupBy(t => t.ParentTaskId!)
                                     .ToDictionary(g => g.Key, g => g.ToList());

        return topLevel.Select(parent =>
        {
            var children = childrenLookup.TryGetValue(parent.Id, out var sub) ? sub : new List<TaskItem>();
            return MapToDto(parent, children);
        }).ToList();
    }

    private static TaskDto MapToDto(TaskItem task, List<TaskItem> subtasks)
    {
        var subtaskDtos = subtasks.Select(s => new TaskDto
        {
            Id = s.Id,
            ListId = s.ListId,
            WorkspaceId = s.WorkspaceId,
            ParentTaskId = s.ParentTaskId,
            Title = s.Title,
            Description = s.Description,
            Status = s.Status,
            Priority = s.Priority,
            DueDate = s.DueDate,
            Position = s.Position,
            CreatedBy = s.CreatedBy,
            CreatedAt = s.CreatedAt,
            UpdatedBy = s.UpdatedBy,
            UpdatedAt = s.UpdatedAt,
            Version = s.Version,
            SubtaskCount = 0,
            CompletedSubtaskCount = 0,
            Subtasks = new List<TaskDto>()
        }).ToList();

        return new TaskDto
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
            SubtaskCount = subtaskDtos.Count,
            CompletedSubtaskCount = subtaskDtos.Count(s => s.Status == "DONE"),
            Subtasks = subtaskDtos
        };
    }

    #endregion
}
