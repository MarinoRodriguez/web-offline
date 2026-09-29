using System;
using System.Collections.Generic;
using WebOffline.Core.Enums;

namespace WebOffline.Core.Entities;

public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string SystemRole { get; set; } = "user"; // "user" | "admin"
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}

public class UserSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string RefreshTokenHash { get; set; } = string.Empty;
    public string? DeviceInfo { get; set; }
    public string? IpAddress { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
    public bool IsRevoked { get; set; } = false;

    public bool IsActiveSession => !IsRevoked && ExpiresAt > DateTime.UtcNow;
}

public class Workspace
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long Version { get; set; } = 1;
    public bool IsDeleted { get; set; } = false;
}

public class WorkspaceMember
{
    public string WorkspaceId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Role { get; set; } = "Editor"; // "Owner", "Editor", "Viewer"
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}

public class TaskList
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string WorkspaceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#3B82F6";
    public int Position { get; set; } = 0;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long Version { get; set; } = 1;
    public bool IsDeleted { get; set; } = false;
}

public class TaskItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? ListId { get; set; }
    public string WorkspaceId { get; set; } = string.Empty;
    public string? ParentTaskId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = "TODO"; // "TODO", "IN_PROGRESS", "DONE", "CANCELLED"
    public string Priority { get; set; } = "MEDIUM"; // "LOW", "MEDIUM", "HIGH", "URGENT"
    public DateTime? DueDate { get; set; }
    public int Position { get; set; } = 0;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long Version { get; set; } = 1;
    public bool IsDeleted { get; set; } = false;
}

public class SyncTombstone
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string EntityType { get; set; } = string.Empty; // "workspace", "list", "task"
    public string EntityId { get; set; } = string.Empty;
    public string? WorkspaceId { get; set; }
    public string DeletedBy { get; set; } = string.Empty;
    public DateTime DeletedAt { get; set; } = DateTime.UtcNow;
}
