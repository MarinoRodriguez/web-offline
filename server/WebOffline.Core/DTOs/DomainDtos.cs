using System;
using System.Collections.Generic;

namespace WebOffline.Core.DTOs;

#region Auth DTOs

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? DeviceInfo { get; set; }
}

public class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? SystemRole { get; set; } // "user" or "admin" (only admin can assign)
}

public class RefreshTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

public class TokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public UserDto User { get; set; } = null!;
}

public class UserDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string SystemRole { get; set; } = "user";
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UserSessionDto
{
    public string Id { get; set; } = string.Empty;
    public string? DeviceInfo { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    public bool IsCurrent { get; set; }
}

#endregion

#region Workspace DTOs

public class CreateWorkspaceRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UpdateWorkspaceRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class WorkspaceDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public string RoleInWorkspace { get; set; } = "Owner";
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long Version { get; set; }
}

public class AddMemberRequest
{
    public string EmailOrUserId { get; set; } = string.Empty;
    public string Role { get; set; } = "Editor";
}

public class WorkspaceMemberDto
{
    public string WorkspaceId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = "Editor";
    public DateTime JoinedAt { get; set; }
}

#endregion

#region List DTOs

public class CreateListRequest
{
    public string WorkspaceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#3B82F6";
    public int Position { get; set; } = 0;
}

public class UpdateListRequest
{
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#3B82F6";
    public int Position { get; set; } = 0;
}

public class ListDto
{
    public string Id { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#3B82F6";
    public int Position { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long Version { get; set; }
}

#endregion

#region Task DTOs

public class CreateTaskRequest
{
    public string ListId { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string? ParentTaskId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Priority { get; set; } = "MEDIUM";
    public DateTime? DueDate { get; set; }
    public int Position { get; set; } = 0;
}

public class UpdateTaskRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = "TODO";
    public string Priority { get; set; } = "MEDIUM";
    public DateTime? DueDate { get; set; }
    public int Position { get; set; }
}

public class UpdateTaskStatusRequest
{
    public string Status { get; set; } = "TODO";
}

public class TaskDto
{
    public string Id { get; set; } = string.Empty;
    public string ListId { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string? ParentTaskId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = "TODO";
    public string Priority { get; set; } = "MEDIUM";
    public DateTime? DueDate { get; set; }
    public int Position { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long Version { get; set; }
    
    // Subtask metrics
    public int SubtaskCount { get; set; }
    public int CompletedSubtaskCount { get; set; }
    public List<TaskDto> Subtasks { get; set; } = new();
}

#endregion
