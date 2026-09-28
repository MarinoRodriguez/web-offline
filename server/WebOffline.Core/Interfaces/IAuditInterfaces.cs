using System.Collections.Generic;
using System.Threading.Tasks;
using WebOffline.Core.Entities;

namespace WebOffline.Core.Interfaces;

public interface IAuditRepository
{
    Task LogRequestAsync(HttpRequestLog log);
    Task<IEnumerable<HttpRequestLog>> GetRequestLogsAsync(int page = 1, int pageSize = 50, string? userId = null, string? path = null);
    Task<int> CountRequestLogsAsync(string? userId = null, string? path = null);

    Task LogChangeAsync(AuditChangeLog changelog);
    Task<IEnumerable<AuditChangeLog>> GetChangeLogsAsync(int page = 1, int pageSize = 50, string? entityType = null, string? entityId = null);
    Task<int> CountChangeLogsAsync(string? entityType = null, string? entityId = null);
    Task<AuditChangeLog?> GetChangeLogByIdAsync(string id);
    Task<string?> GetLatestChangeLogHashAsync();
}

public interface IAuditService
{
    Task<AuditChangeLog> RecordChangeAsync<T>(
        string userId,
        string userEmail,
        string action,
        string entityType,
        string entityId,
        string description,
        T? oldState,
        T? newState);

    string CalculateTamperHash(
        string id,
        string timestampIso,
        string userId,
        string userEmail,
        string action,
        string entityType,
        string entityId,
        string? diffJson,
        string? prevHash);

    bool VerifyChangeLogIntegrity(AuditChangeLog log);
}
