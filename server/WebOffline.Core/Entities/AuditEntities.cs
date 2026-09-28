using System;

namespace WebOffline.Core.Entities;

public class HttpRequestLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
    public string? UserId { get; set; }
    public string? UserEmail { get; set; }
    public string? ClientIp { get; set; }
    public string? UserAgent { get; set; }
    public string HttpMethod { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? QueryString { get; set; }
    public string? RequestBody { get; set; }
    public int StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public long DurationMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AuditChangeLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string UserId { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // CREATE, UPDATE, DELETE, STATUS_CHANGE
    public string EntityType { get; set; } = string.Empty; // Workspace, List, Task, User
    public string EntityId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
    public string? DiffJson { get; set; }
    public string? PrevHash { get; set; }
    public string TamperHash { get; set; } = string.Empty;
}
