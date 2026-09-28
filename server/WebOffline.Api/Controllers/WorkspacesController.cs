using System;
using System.Collections.Generic;
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
public class WorkspacesController : BaseApiController
{
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IUserRepository _userRepository;
    private readonly IAbacPolicyEvaluator _abacEvaluator;

    public WorkspacesController(
        IWorkspaceRepository workspaceRepository,
        IUserRepository userRepository,
        IAbacPolicyEvaluator abacEvaluator)
    {
        _workspaceRepository = workspaceRepository;
        _userRepository = userRepository;
        _abacEvaluator = abacEvaluator;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<WorkspaceDto>>>> GetMyWorkspaces()
    {
        var workspaces = await _workspaceRepository.GetUserWorkspacesAsync(CurrentUserId);
        return Ok(ApiResponse<IEnumerable<WorkspaceDto>>.Ok(workspaces));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<WorkspaceDto>>> GetById(string id)
    {
        var canRead = await _abacEvaluator.CanAccessWorkspaceAsync(CurrentUserId, id, ResourceAction.Read, IsSystemAdmin);
        if (!canRead)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<WorkspaceDto>.Fail("Access denied to this workspace.", 403));
        }

        var ws = await _workspaceRepository.GetByIdAsync(id);
        if (ws == null)
        {
            return NotFound(ApiResponse<WorkspaceDto>.Fail("Workspace not found.", 404));
        }

        var role = await _workspaceRepository.GetUserRoleInWorkspaceAsync(id, CurrentUserId) ?? (ws.OwnerId == CurrentUserId ? "Owner" : "Viewer");

        var dto = new WorkspaceDto
        {
            Id = ws.Id,
            Name = ws.Name,
            Description = ws.Description,
            OwnerId = ws.OwnerId,
            RoleInWorkspace = role,
            CreatedAt = ws.CreatedAt,
            UpdatedAt = ws.UpdatedAt,
            Version = ws.Version
        };

        return Ok(ApiResponse<WorkspaceDto>.Ok(dto));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<WorkspaceDto>>> Create([FromBody] CreateWorkspaceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(ApiResponse<WorkspaceDto>.Fail("Workspace name is required."));
        }

        var workspace = new Workspace
        {
            Id = Guid.NewGuid().ToString(),
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            OwnerId = CurrentUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1,
            IsDeleted = false
        };

        await _workspaceRepository.CreateAsync(workspace);

        var dto = new WorkspaceDto
        {
            Id = workspace.Id,
            Name = workspace.Name,
            Description = workspace.Description,
            OwnerId = workspace.OwnerId,
            RoleInWorkspace = "Owner",
            CreatedAt = workspace.CreatedAt,
            UpdatedAt = workspace.UpdatedAt,
            Version = workspace.Version
        };

        return CreatedAtAction(nameof(GetById), new { id = workspace.Id }, ApiResponse<WorkspaceDto>.Ok(dto, "Workspace created successfully", 201));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<WorkspaceDto>>> Update(string id, [FromBody] UpdateWorkspaceRequest request)
    {
        var canWrite = await _abacEvaluator.CanAccessWorkspaceAsync(CurrentUserId, id, ResourceAction.Write, IsSystemAdmin);
        if (!canWrite)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<WorkspaceDto>.Fail("Access denied to modify this workspace.", 403));
        }

        var ws = await _workspaceRepository.GetByIdAsync(id);
        if (ws == null)
        {
            return NotFound(ApiResponse<WorkspaceDto>.Fail("Workspace not found.", 404));
        }

        ws.Name = request.Name.Trim();
        ws.Description = request.Description?.Trim();

        await _workspaceRepository.UpdateAsync(ws);

        var role = await _workspaceRepository.GetUserRoleInWorkspaceAsync(id, CurrentUserId) ?? "Editor";
        var dto = new WorkspaceDto
        {
            Id = ws.Id,
            Name = ws.Name,
            Description = ws.Description,
            OwnerId = ws.OwnerId,
            RoleInWorkspace = role,
            CreatedAt = ws.CreatedAt,
            UpdatedAt = DateTime.UtcNow,
            Version = ws.Version + 1
        };

        return Ok(ApiResponse<WorkspaceDto>.Ok(dto, "Workspace updated successfully"));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse>> Delete(string id)
    {
        var canDelete = await _abacEvaluator.CanAccessWorkspaceAsync(CurrentUserId, id, ResourceAction.Delete, IsSystemAdmin);
        if (!canDelete)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail("Access denied: Only workspace owner can delete it.", 403));
        }

        await _workspaceRepository.SoftDeleteAsync(id);
        return Ok(ApiResponse.Ok("Workspace deleted successfully"));
    }

    [HttpGet("{id}/members")]
    public async Task<ActionResult<ApiResponse<IEnumerable<WorkspaceMemberDto>>>> GetMembers(string id)
    {
        var canRead = await _abacEvaluator.CanAccessWorkspaceAsync(CurrentUserId, id, ResourceAction.Read, IsSystemAdmin);
        if (!canRead)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<IEnumerable<WorkspaceMemberDto>>.Fail("Access denied.", 403));
        }

        var members = await _workspaceRepository.GetMembersAsync(id);
        return Ok(ApiResponse<IEnumerable<WorkspaceMemberDto>>.Ok(members));
    }

    [HttpPost("{id}/members")]
    public async Task<ActionResult<ApiResponse>> AddMember(string id, [FromBody] AddMemberRequest request)
    {
        var canAdmin = await _abacEvaluator.CanAccessWorkspaceAsync(CurrentUserId, id, ResourceAction.Admin, IsSystemAdmin);
        if (!canAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail("Access denied: Only workspace owner can manage members.", 403));
        }

        User? targetUser = await _userRepository.GetByIdAsync(request.EmailOrUserId);
        if (targetUser == null)
        {
            targetUser = await _userRepository.GetByEmailAsync(request.EmailOrUserId);
        }

        if (targetUser == null)
        {
            return NotFound(ApiResponse.Fail("User not found.", 404));
        }

        var member = new WorkspaceMember
        {
            WorkspaceId = id,
            UserId = targetUser.Id,
            Role = request.Role,
            JoinedAt = DateTime.UtcNow
        };

        await _workspaceRepository.AddMemberAsync(member);
        return Ok(ApiResponse.Ok("Member added or updated successfully"));
    }

    [HttpDelete("{id}/members/{memberUserId}")]
    public async Task<ActionResult<ApiResponse>> RemoveMember(string id, string memberUserId)
    {
        var canAdmin = await _abacEvaluator.CanAccessWorkspaceAsync(CurrentUserId, id, ResourceAction.Admin, IsSystemAdmin);
        if (!canAdmin && CurrentUserId != memberUserId) // Allow leaving workspace yourself
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail("Access denied.", 403));
        }

        await _workspaceRepository.RemoveMemberAsync(id, memberUserId);
        return Ok(ApiResponse.Ok("Member removed successfully"));
    }
}
