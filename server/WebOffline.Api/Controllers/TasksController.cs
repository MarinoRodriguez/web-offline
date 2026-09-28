using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WebOffline.Api.Security.Abac;
using WebOffline.Core.Common;
using WebOffline.Core.DTOs;
using WebOffline.Core.Entities;
using WebOffline.Core.Interfaces;

namespace WebOffline.Api.Controllers;

[Authorize]
[Route("api/[controller]")]
public class TasksController : BaseApiController
{
    private readonly ITaskRepository _taskRepository;
    private readonly IListRepository _listRepository;
    private readonly IAbacPolicyEvaluator _abacEvaluator;
    private readonly IAuditService _auditService;

    public TasksController(
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

    [HttpGet("workspace/{workspaceId}")]
    public async Task<ActionResult<ApiResponse<IEnumerable<TaskDto>>>> GetByWorkspace(string workspaceId)
    {
        var canRead = await _abacEvaluator.CanAccessWorkspaceAsync(CurrentUserId, workspaceId, ResourceAction.Read, IsSystemAdmin);
        if (!canRead)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<IEnumerable<TaskDto>>.Fail("Access denied.", 403));
        }

        var allTasks = (await _taskRepository.GetByWorkspaceIdAsync(workspaceId)).ToList();
        var dtos = BuildTaskHierarchy(allTasks);

        return Ok(ApiResponse<IEnumerable<TaskDto>>.Ok(dtos));
    }

    [HttpGet("list/{listId}")]
    public async Task<ActionResult<ApiResponse<IEnumerable<TaskDto>>>> GetByList(string listId)
    {
        var canRead = await _abacEvaluator.CanAccessListAsync(CurrentUserId, listId, ResourceAction.Read, IsSystemAdmin);
        if (!canRead)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<IEnumerable<TaskDto>>.Fail("Access denied.", 403));
        }

        var tasks = (await _taskRepository.GetByListIdAsync(listId)).ToList();
        var dtos = BuildTaskHierarchy(tasks);

        return Ok(ApiResponse<IEnumerable<TaskDto>>.Ok(dtos));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<TaskDto>>> GetById(string id)
    {
        var task = await _taskRepository.GetByIdAsync(id);
        if (task == null)
        {
            return NotFound(ApiResponse<TaskDto>.Fail("Task not found.", 404));
        }

        var canRead = await _abacEvaluator.CanAccessWorkspaceAsync(CurrentUserId, task.WorkspaceId, ResourceAction.Read, IsSystemAdmin);
        if (!canRead)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<TaskDto>.Fail("Access denied.", 403));
        }

        var subtasks = (await _taskRepository.GetSubtasksAsync(id)).ToList();
        var dto = MapToDto(task, subtasks);

        return Ok(ApiResponse<TaskDto>.Ok(dto));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<TaskDto>>> Create([FromBody] CreateTaskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.ListId))
        {
            return BadRequest(ApiResponse<TaskDto>.Fail("Title and ListId are required."));
        }

        var list = await _listRepository.GetByIdAsync(request.ListId);
        if (list == null)
        {
            return NotFound(ApiResponse<TaskDto>.Fail("Parent list not found.", 404));
        }

        var workspaceId = string.IsNullOrWhiteSpace(request.WorkspaceId) ? list.WorkspaceId : request.WorkspaceId;

        var canWrite = await _abacEvaluator.CanAccessWorkspaceAsync(CurrentUserId, workspaceId, ResourceAction.Write, IsSystemAdmin);
        if (!canWrite)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<TaskDto>.Fail("Access denied to create tasks in this workspace.", 403));
        }

        if (!string.IsNullOrWhiteSpace(request.ParentTaskId))
        {
            var parentTask = await _taskRepository.GetByIdAsync(request.ParentTaskId);
            if (parentTask == null)
            {
                return NotFound(ApiResponse<TaskDto>.Fail("Parent task not found.", 404));
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
            CreatedBy = CurrentUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedBy = CurrentUserId,
            UpdatedAt = DateTime.UtcNow,
            Version = 1,
            IsDeleted = false
        };

        await _taskRepository.CreateAsync(task);

        // Audit changelog
        var actionLabel = string.IsNullOrWhiteSpace(task.ParentTaskId) ? "tarea" : "subtarea";
        await _auditService.RecordChangeAsync(
            CurrentUserId,
            CurrentUserEmail,
            "CREATE",
            "Task",
            task.Id,
            $"{CurrentUserEmail} creó la {actionLabel} '{task.Title}'",
            (TaskItem?)null,
            task);

        var dto = MapToDto(task, new List<TaskItem>());
        return CreatedAtAction(nameof(GetById), new { id = task.Id }, ApiResponse<TaskDto>.Ok(dto, "Task created successfully", 201));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<TaskDto>>> Update(string id, [FromBody] UpdateTaskRequest request)
    {
        var task = await _taskRepository.GetByIdAsync(id);
        if (task == null)
        {
            return NotFound(ApiResponse<TaskDto>.Fail("Task not found.", 404));
        }

        var canWrite = await _abacEvaluator.CanAccessWorkspaceAsync(CurrentUserId, task.WorkspaceId, ResourceAction.Write, IsSystemAdmin);
        if (!canWrite)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<TaskDto>.Fail("Access denied.", 403));
        }

        var targetStatus = request.Status?.ToUpperInvariant() ?? task.Status;

        if (targetStatus == "DONE" && task.Status != "DONE")
        {
            var allCompleted = await _taskRepository.AreAllSubtasksCompletedAsync(id);
            if (!allCompleted)
            {
                var pendingCount = await _taskRepository.CountPendingSubtasksAsync(id);
                return BadRequest(ApiResponse<TaskDto>.Fail(
                    $"Cannot complete task: There are {pendingCount} incomplete subtasks that must be completed first.", 400));
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
        task.UpdatedBy = CurrentUserId;
        task.UpdatedAt = DateTime.UtcNow;

        await _taskRepository.UpdateAsync(task);

        // Audit changelog
        await _auditService.RecordChangeAsync(
            CurrentUserId,
            CurrentUserEmail,
            "UPDATE",
            "Task",
            task.Id,
            $"{CurrentUserEmail} modificó la tarea '{task.Title}'",
            oldState,
            task);

        var subtasks = (await _taskRepository.GetSubtasksAsync(id)).ToList();
        var dto = MapToDto(task, subtasks);

        return Ok(ApiResponse<TaskDto>.Ok(dto, "Task updated successfully"));
    }

    [HttpPatch("{id}/status")]
    public async Task<ActionResult<ApiResponse<TaskDto>>> UpdateStatus(string id, [FromBody] UpdateTaskStatusRequest request)
    {
        var task = await _taskRepository.GetByIdAsync(id);
        if (task == null)
        {
            return NotFound(ApiResponse<TaskDto>.Fail("Task not found.", 404));
        }

        var canWrite = await _abacEvaluator.CanAccessWorkspaceAsync(CurrentUserId, task.WorkspaceId, ResourceAction.Write, IsSystemAdmin);
        if (!canWrite)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<TaskDto>.Fail("Access denied.", 403));
        }

        var targetStatus = request.Status?.ToUpperInvariant() ?? "TODO";

        if (targetStatus == "DONE" && task.Status != "DONE")
        {
            var allCompleted = await _taskRepository.AreAllSubtasksCompletedAsync(id);
            if (!allCompleted)
            {
                var pendingCount = await _taskRepository.CountPendingSubtasksAsync(id);
                return BadRequest(ApiResponse<TaskDto>.Fail(
                    $"Cannot complete task: There are {pendingCount} incomplete subtasks that must be completed first.", 400));
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
        await _taskRepository.UpdateStatusAsync(id, targetStatus, newVersion, CurrentUserId);

        task.Status = targetStatus;
        task.Version = newVersion;
        task.UpdatedBy = CurrentUserId;
        task.UpdatedAt = DateTime.UtcNow;

        // Audit changelog
        await _auditService.RecordChangeAsync(
            CurrentUserId,
            CurrentUserEmail,
            "STATUS_CHANGE",
            "Task",
            task.Id,
            $"{CurrentUserEmail} cambió el estado de la tarea '{task.Title}' a '{targetStatus}'",
            oldState,
            task);

        var subtasks = (await _taskRepository.GetSubtasksAsync(id)).ToList();
        var dto = MapToDto(task, subtasks);

        return Ok(ApiResponse<TaskDto>.Ok(dto, "Status updated successfully"));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse>> Delete(string id)
    {
        var task = await _taskRepository.GetByIdAsync(id);
        if (task == null)
        {
            return NotFound(ApiResponse.Fail("Task not found.", 404));
        }

        var canWrite = await _abacEvaluator.CanAccessWorkspaceAsync(CurrentUserId, task.WorkspaceId, ResourceAction.Write, IsSystemAdmin);
        if (!canWrite)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail("Access denied.", 403));
        }

        await _taskRepository.SoftDeleteAsync(id, CurrentUserId);

        // Audit changelog
        await _auditService.RecordChangeAsync(
            CurrentUserId,
            CurrentUserEmail,
            "DELETE",
            "Task",
            id,
            $"{CurrentUserEmail} eliminó la tarea '{task.Title}'",
            task,
            (TaskItem?)null);

        return Ok(ApiResponse.Ok("Task deleted successfully"));
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
