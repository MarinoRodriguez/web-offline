using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WebOffline.Core.Common;
using WebOffline.Core.DTOs;
using WebOffline.Core.Entities;
using WebOffline.Core.Interfaces;

namespace WebOffline.Infrastructure.Services;

public class WorkspaceService : IWorkspaceService
{
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IUserRepository _userRepository;
    private readonly IAbacPolicyEvaluator _abacEvaluator;
    private readonly IAuditService _auditService;

    public WorkspaceService(
        IWorkspaceRepository workspaceRepository,
        IUserRepository userRepository,
        IAbacPolicyEvaluator abacEvaluator,
        IAuditService auditService)
    {
        _workspaceRepository = workspaceRepository;
        _userRepository = userRepository;
        _abacEvaluator = abacEvaluator;
        _auditService = auditService;
    }

    public async Task<ApiResponse<IEnumerable<WorkspaceDto>>> GetUserWorkspacesAsync(string currentUserId)
    {
        var workspaces = await _workspaceRepository.GetUserWorkspacesAsync(currentUserId);
        return ApiResponse<IEnumerable<WorkspaceDto>>.Ok(workspaces);
    }

    public async Task<ApiResponse<WorkspaceDto>> GetWorkspaceByIdAsync(string workspaceId, string currentUserId, bool isSystemAdmin)
    {
        var canRead = await _abacEvaluator.CanAccessWorkspaceAsync(currentUserId, workspaceId, ResourceAction.Read, isSystemAdmin);
        if (!canRead)
        {
            return ApiResponse<WorkspaceDto>.Fail("Access denied to this workspace.", 403);
        }

        var ws = await _workspaceRepository.GetByIdAsync(workspaceId);
        if (ws == null)
        {
            return ApiResponse<WorkspaceDto>.Fail("Workspace not found.", 404);
        }

        var role = await _workspaceRepository.GetUserRoleInWorkspaceAsync(workspaceId, currentUserId) 
            ?? (ws.OwnerId == currentUserId ? "Owner" : "Viewer");

        var dto = new WorkspaceDto
        {
            Id = ws.Id,
            Name = ws.Name,
            Description = ws.Description,
            OwnerId = ws.OwnerId,
            RoleInWorkspace = role,
            CreatedBy = ws.CreatedBy,
            CreatedAt = ws.CreatedAt,
            UpdatedBy = ws.UpdatedBy,
            UpdatedAt = ws.UpdatedAt,
            Version = ws.Version
        };

        return ApiResponse<WorkspaceDto>.Ok(dto);
    }

    public async Task<ApiResponse<WorkspaceDto>> CreateWorkspaceAsync(CreateWorkspaceRequest request, string currentUserId, string currentUserEmail)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ApiResponse<WorkspaceDto>.Fail("Workspace name is required.", 400);
        }

        var workspace = new Workspace
        {
            Id = Guid.NewGuid().ToString(),
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            OwnerId = currentUserId,
            CreatedBy = currentUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedBy = currentUserId,
            UpdatedAt = DateTime.UtcNow,
            Version = 1,
            IsDeleted = false
        };

        await _workspaceRepository.CreateAsync(workspace);

        // Audit changelog
        await _auditService.RecordChangeAsync(
            currentUserId,
            currentUserEmail,
            "CREATE",
            "Workspace",
            workspace.Id,
            $"{currentUserEmail} creó el workspace '{workspace.Name}'",
            (Workspace?)null,
            workspace);

        var dto = new WorkspaceDto
        {
            Id = workspace.Id,
            Name = workspace.Name,
            Description = workspace.Description,
            OwnerId = workspace.OwnerId,
            RoleInWorkspace = "Owner",
            CreatedBy = workspace.CreatedBy,
            CreatedAt = workspace.CreatedAt,
            UpdatedBy = workspace.UpdatedBy,
            UpdatedAt = workspace.UpdatedAt,
            Version = workspace.Version
        };

        return ApiResponse<WorkspaceDto>.Ok(dto, "Workspace created successfully", 201);
    }

    public async Task<ApiResponse<WorkspaceDto>> UpdateWorkspaceAsync(string workspaceId, UpdateWorkspaceRequest request, string currentUserId, string currentUserEmail, bool isSystemAdmin)
    {
        var canWrite = await _abacEvaluator.CanAccessWorkspaceAsync(currentUserId, workspaceId, ResourceAction.Write, isSystemAdmin);
        if (!canWrite)
        {
            return ApiResponse<WorkspaceDto>.Fail("Access denied to modify this workspace.", 403);
        }

        var ws = await _workspaceRepository.GetByIdAsync(workspaceId);
        if (ws == null)
        {
            return ApiResponse<WorkspaceDto>.Fail("Workspace not found.", 404);
        }

        var oldState = new Workspace
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

        ws.Name = request.Name.Trim();
        ws.Description = request.Description?.Trim();
        ws.UpdatedBy = currentUserId;
        ws.UpdatedAt = DateTime.UtcNow;

        await _workspaceRepository.UpdateAsync(ws);

        // Audit changelog
        await _auditService.RecordChangeAsync(
            currentUserId,
            currentUserEmail,
            "UPDATE",
            "Workspace",
            ws.Id,
            $"{currentUserEmail} modificó el workspace '{ws.Name}'",
            oldState,
            ws);

        var role = await _workspaceRepository.GetUserRoleInWorkspaceAsync(workspaceId, currentUserId) ?? "Editor";
        var dto = new WorkspaceDto
        {
            Id = ws.Id,
            Name = ws.Name,
            Description = ws.Description,
            OwnerId = ws.OwnerId,
            RoleInWorkspace = role,
            CreatedBy = ws.CreatedBy,
            CreatedAt = ws.CreatedAt,
            UpdatedBy = ws.UpdatedBy,
            UpdatedAt = ws.UpdatedAt,
            Version = ws.Version + 1
        };

        return ApiResponse<WorkspaceDto>.Ok(dto, "Workspace updated successfully");
    }

    public async Task<ApiResponse> DeleteWorkspaceAsync(string workspaceId, string currentUserId, string currentUserEmail, bool isSystemAdmin)
    {
        var canDelete = await _abacEvaluator.CanAccessWorkspaceAsync(currentUserId, workspaceId, ResourceAction.Delete, isSystemAdmin);
        if (!canDelete)
        {
            return ApiResponse.Fail("Access denied: Only workspace owner can delete it.", 403);
        }

        var ws = await _workspaceRepository.GetByIdAsync(workspaceId);
        if (ws != null)
        {
            await _workspaceRepository.SoftDeleteAsync(workspaceId, currentUserId);

            // Audit changelog
            await _auditService.RecordChangeAsync(
                currentUserId,
                currentUserEmail,
                "DELETE",
                "Workspace",
                workspaceId,
                $"{currentUserEmail} eliminó el workspace '{ws.Name}'",
                ws,
                (Workspace?)null);
        }

        return ApiResponse.Ok("Workspace deleted successfully");
    }

    public async Task<ApiResponse<IEnumerable<WorkspaceMemberDto>>> GetMembersAsync(string workspaceId, string currentUserId, bool isSystemAdmin)
    {
        var canRead = await _abacEvaluator.CanAccessWorkspaceAsync(currentUserId, workspaceId, ResourceAction.Read, isSystemAdmin);
        if (!canRead)
        {
            return ApiResponse<IEnumerable<WorkspaceMemberDto>>.Fail("Access denied.", 403);
        }

        var members = await _workspaceRepository.GetMembersAsync(workspaceId);
        return ApiResponse<IEnumerable<WorkspaceMemberDto>>.Ok(members);
    }

    public async Task<ApiResponse> AddMemberAsync(string workspaceId, AddMemberRequest request, string currentUserId, string currentUserEmail, bool isSystemAdmin)
    {
        var canAdmin = await _abacEvaluator.CanAccessWorkspaceAsync(currentUserId, workspaceId, ResourceAction.Admin, isSystemAdmin);
        if (!canAdmin)
        {
            return ApiResponse.Fail("Access denied: Only workspace owner can manage members.", 403);
        }

        User? targetUser = await _userRepository.GetByIdAsync(request.EmailOrUserId);
        if (targetUser == null)
        {
            targetUser = await _userRepository.GetByEmailAsync(request.EmailOrUserId);
        }

        if (targetUser == null)
        {
            return ApiResponse.Fail("User not found.", 404);
        }

        var member = new WorkspaceMember
        {
            WorkspaceId = workspaceId,
            UserId = targetUser.Id,
            Role = request.Role,
            JoinedAt = DateTime.UtcNow
        };

        await _workspaceRepository.AddMemberAsync(member);

        await _auditService.RecordChangeAsync(
            currentUserId,
            currentUserEmail,
            "ADD_MEMBER",
            "WorkspaceMember",
            $"{workspaceId}_{targetUser.Id}",
            $"{currentUserEmail} agregó al miembro '{targetUser.Email}' con rol '{request.Role}' al workspace",
            (WorkspaceMember?)null,
            member);

        return ApiResponse.Ok("Member added or updated successfully");
    }

    public async Task<ApiResponse> RemoveMemberAsync(string workspaceId, string memberUserId, string currentUserId, string currentUserEmail, bool isSystemAdmin)
    {
        var canAdmin = await _abacEvaluator.CanAccessWorkspaceAsync(currentUserId, workspaceId, ResourceAction.Admin, isSystemAdmin);
        if (!canAdmin && currentUserId != memberUserId) // Allow leaving workspace yourself
        {
            return ApiResponse.Fail("Access denied.", 403);
        }

        await _workspaceRepository.RemoveMemberAsync(workspaceId, memberUserId);

        await _auditService.RecordChangeAsync(
            currentUserId,
            currentUserEmail,
            "REMOVE_MEMBER",
            "WorkspaceMember",
            $"{workspaceId}_{memberUserId}",
            $"{currentUserEmail} eliminó al miembro '{memberUserId}' del workspace",
            new { WorkspaceId = workspaceId, UserId = memberUserId },
            (object?)null);

        return ApiResponse.Ok("Member removed successfully");
    }
}
