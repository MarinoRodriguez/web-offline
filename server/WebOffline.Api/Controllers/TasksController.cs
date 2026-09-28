using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebOffline.Core.Common;
using WebOffline.Core.DTOs;
using WebOffline.Core.Interfaces;

namespace WebOffline.Api.Controllers;

[Authorize]
[Route("api/[controller]")]
public class TasksController : BaseApiController
{
    private readonly ITaskService _taskService;

    public TasksController(ITaskService taskService)
    {
        _taskService = taskService;
    }

    [HttpGet("workspace/{workspaceId}")]
    public async Task<ActionResult<ApiResponse<IEnumerable<TaskDto>>>> GetByWorkspace(string workspaceId)
    {
        var result = await _taskService.GetTasksByWorkspaceAsync(workspaceId, CurrentUserId, IsSystemAdmin);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("list/{listId}")]
    public async Task<ActionResult<ApiResponse<IEnumerable<TaskDto>>>> GetByList(string listId)
    {
        var result = await _taskService.GetTasksByListAsync(listId, CurrentUserId, IsSystemAdmin);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<TaskDto>>> GetById(string id)
    {
        var result = await _taskService.GetTaskByIdAsync(id, CurrentUserId, IsSystemAdmin);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<TaskDto>>> Create([FromBody] CreateTaskRequest request)
    {
        var result = await _taskService.CreateTaskAsync(request, CurrentUserId, CurrentUserEmail, IsSystemAdmin);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<TaskDto>>> Update(string id, [FromBody] UpdateTaskRequest request)
    {
        var result = await _taskService.UpdateTaskAsync(id, request, CurrentUserId, CurrentUserEmail, IsSystemAdmin);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPatch("{id}/status")]
    public async Task<ActionResult<ApiResponse<TaskDto>>> UpdateStatus(string id, [FromBody] UpdateTaskStatusRequest request)
    {
        var result = await _taskService.UpdateTaskStatusAsync(id, request, CurrentUserId, CurrentUserEmail, IsSystemAdmin);
        return StatusCode(result.StatusCode, result);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse>> Delete(string id)
    {
        var result = await _taskService.DeleteTaskAsync(id, CurrentUserId, CurrentUserEmail, IsSystemAdmin);
        return StatusCode(result.StatusCode, result);
    }
}
