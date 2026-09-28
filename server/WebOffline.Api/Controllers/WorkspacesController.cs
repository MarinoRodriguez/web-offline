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
public class WorkspacesController : BaseApiController
{
    private readonly IWorkspaceService _workspaceService;

    public WorkspacesController(IWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<WorkspaceDto>>>> GetMyWorkspaces()
    {
        var result = await _workspaceService.GetUserWorkspacesAsync(CurrentUserId);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<WorkspaceDto>>> GetById(string id)
    {
        var result = await _workspaceService.GetWorkspaceByIdAsync(id, CurrentUserId, IsSystemAdmin);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<WorkspaceDto>>> Create([FromBody] CreateWorkspaceRequest request)
    {
        var result = await _workspaceService.CreateWorkspaceAsync(request, CurrentUserId, CurrentUserEmail);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<WorkspaceDto>>> Update(string id, [FromBody] UpdateWorkspaceRequest request)
    {
        var result = await _workspaceService.UpdateWorkspaceAsync(id, request, CurrentUserId, CurrentUserEmail, IsSystemAdmin);
        return StatusCode(result.StatusCode, result);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse>> Delete(string id)
    {
        var result = await _workspaceService.DeleteWorkspaceAsync(id, CurrentUserId, CurrentUserEmail, IsSystemAdmin);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{id}/members")]
    public async Task<ActionResult<ApiResponse<IEnumerable<WorkspaceMemberDto>>>> GetMembers(string id)
    {
        var result = await _workspaceService.GetMembersAsync(id, CurrentUserId, IsSystemAdmin);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{id}/members")]
    public async Task<ActionResult<ApiResponse>> AddMember(string id, [FromBody] AddMemberRequest request)
    {
        var result = await _workspaceService.AddMemberAsync(id, request, CurrentUserId, CurrentUserEmail, IsSystemAdmin);
        return StatusCode(result.StatusCode, result);
    }

    [HttpDelete("{id}/members/{userId}")]
    public async Task<ActionResult<ApiResponse>> RemoveMember(string id, string userId)
    {
        var result = await _workspaceService.RemoveMemberAsync(id, userId, CurrentUserId, CurrentUserEmail, IsSystemAdmin);
        return StatusCode(result.StatusCode, result);
    }
}
