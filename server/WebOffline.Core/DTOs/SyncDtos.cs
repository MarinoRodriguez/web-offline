using System;
using System.Collections.Generic;
using System.Text.Json;

namespace WebOffline.Core.DTOs;

public class SyncMutationDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Entity { get; set; } = string.Empty; // "workspace" | "list" | "task"
    public string Operation { get; set; } = string.Empty; // "INSERT" | "UPDATE" | "DELETE"
    public string EntityId { get; set; } = string.Empty;
    public string? WorkspaceId { get; set; }
    public DateTime ClientTimestamp { get; set; } = DateTime.UtcNow;
    public long Version { get; set; } = 1;
    public JsonElement? Payload { get; set; }
}

public class SyncRejectionDto
{
    public string MutationId { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int StatusCode { get; set; } = 400;
}

public class SyncTombstoneDto
{
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? WorkspaceId { get; set; }
    public DateTime DeletedAt { get; set; }
}

public class SyncChangesDto
{
    public List<WorkspaceDto> Workspaces { get; set; } = new();
    public List<ListDto> Lists { get; set; } = new();
    public List<TaskDto> Tasks { get; set; } = new();
    public List<SyncTombstoneDto> Tombstones { get; set; } = new();
}

public class SyncBatchRequest
{
    public DateTime? LastSyncedAt { get; set; }
    public string? WorkspaceId { get; set; }
    public List<SyncMutationDto> Mutations { get; set; } = new();
}

public class SyncBatchResponse
{
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
    public int AppliedCount { get; set; }
    public List<SyncRejectionDto> Rejections { get; set; } = new();
    public SyncChangesDto Changes { get; set; } = new();
}
