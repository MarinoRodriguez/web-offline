using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using WebOffline.Core.Entities;
using WebOffline.Core.Interfaces;
using WebOffline.Infrastructure.Data;

namespace WebOffline.Infrastructure.Repositories;

public class AuditRepository : IAuditRepository
{
    private readonly IAuditDbConnectionFactory _connectionFactory;

    public AuditRepository(IAuditDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task LogRequestAsync(HttpRequestLog log)
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO http_request_logs (
                id, correlation_id, user_id, user_email, client_ip, user_agent,
                http_method, path, query_string, request_headers, request_body, status_code,
                response_headers, response_body, duration_ms, created_at
            ) VALUES (
                @Id, @CorrelationId, @UserId, @UserEmail, @ClientIp, @UserAgent,
                @HttpMethod, @Path, @QueryString, @RequestHeaders, @RequestBody, @StatusCode,
                @ResponseHeaders, @ResponseBody, @DurationMs, @CreatedAt
            );";

        await connection.ExecuteAsync(sql, new
        {
            log.Id,
            log.CorrelationId,
            log.UserId,
            log.UserEmail,
            log.ClientIp,
            log.UserAgent,
            log.HttpMethod,
            log.Path,
            log.QueryString,
            log.RequestHeaders,
            log.RequestBody,
            log.StatusCode,
            log.ResponseHeaders,
            log.ResponseBody,
            log.DurationMs,
            CreatedAt = log.CreatedAt.ToString("O")
        });
    }

    public async Task<IEnumerable<HttpRequestLog>> GetRequestLogsAsync(int page = 1, int pageSize = 50, string? userId = null, string? path = null)
    {
        using var connection = _connectionFactory.CreateConnection();
        var offset = (page - 1) * pageSize;

        var sql = @"
            SELECT id, correlation_id as CorrelationId, user_id as UserId, user_email as UserEmail,
                   client_ip as ClientIp, user_agent as UserAgent, http_method as HttpMethod,
                   path, query_string as QueryString, request_headers as RequestHeaders,
                   request_body as RequestBody, status_code as StatusCode,
                   response_headers as ResponseHeaders, response_body as ResponseBody,
                   duration_ms as DurationMs, created_at as CreatedAt
            FROM http_request_logs
            WHERE (@UserId IS NULL OR user_id = @UserId)
              AND (@Path IS NULL OR path LIKE '%' || @Path || '%')
            ORDER BY created_at DESC
            LIMIT @PageSize OFFSET @Offset;";

        return await connection.QueryAsync<HttpRequestLog>(sql, new { UserId = userId, Path = path, PageSize = pageSize, Offset = offset });
    }

    public async Task<int> CountRequestLogsAsync(string? userId = null, string? path = null)
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = @"
            SELECT COUNT(1) FROM http_request_logs
            WHERE (@UserId IS NULL OR user_id = @UserId)
              AND (@Path IS NULL OR path LIKE '%' || @Path || '%');";

        return await connection.ExecuteScalarAsync<int>(sql, new { UserId = userId, Path = path });
    }

    public async Task LogChangeAsync(AuditChangeLog changelog)
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO audit_changelogs (
                id, timestamp, user_id, user_email, action, entity_type, entity_id,
                description, old_values_json, new_values_json, diff_json, prev_hash, tamper_hash
            ) VALUES (
                @Id, @Timestamp, @UserId, @UserEmail, @Action, @EntityType, @EntityId,
                @Description, @OldValuesJson, @NewValuesJson, @DiffJson, @PrevHash, @TamperHash
            );";

        await connection.ExecuteAsync(sql, new
        {
            changelog.Id,
            Timestamp = changelog.Timestamp.ToString("O"),
            changelog.UserId,
            changelog.UserEmail,
            changelog.Action,
            changelog.EntityType,
            changelog.EntityId,
            changelog.Description,
            changelog.OldValuesJson,
            changelog.NewValuesJson,
            changelog.DiffJson,
            changelog.PrevHash,
            changelog.TamperHash
        });
    }

    public async Task<IEnumerable<AuditChangeLog>> GetChangeLogsAsync(int page = 1, int pageSize = 50, string? entityType = null, string? entityId = null)
    {
        using var connection = _connectionFactory.CreateConnection();
        var offset = (page - 1) * pageSize;

        var sql = @"
            SELECT id, timestamp, user_id as UserId, user_email as UserEmail,
                   action, entity_type as EntityType, entity_id as EntityId,
                   description, old_values_json as OldValuesJson, new_values_json as NewValuesJson,
                   diff_json as DiffJson, prev_hash as PrevHash, tamper_hash as TamperHash
            FROM audit_changelogs
            WHERE (@EntityType IS NULL OR entity_type = @EntityType)
              AND (@EntityId IS NULL OR entity_id = @EntityId)
            ORDER BY timestamp DESC
            LIMIT @PageSize OFFSET @Offset;";

        return await connection.QueryAsync<AuditChangeLog>(sql, new { EntityType = entityType, EntityId = entityId, PageSize = pageSize, Offset = offset });
    }

    public async Task<int> CountChangeLogsAsync(string? entityType = null, string? entityId = null)
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = @"
            SELECT COUNT(1) FROM audit_changelogs
            WHERE (@EntityType IS NULL OR entity_type = @EntityType)
              AND (@EntityId IS NULL OR entity_id = @EntityId);";

        return await connection.ExecuteScalarAsync<int>(sql, new { EntityType = entityType, EntityId = entityId });
    }

    public async Task<AuditChangeLog?> GetChangeLogByIdAsync(string id)
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = @"
            SELECT id, timestamp, user_id as UserId, user_email as UserEmail,
                   action, entity_type as EntityType, entity_id as EntityId,
                   description, old_values_json as OldValuesJson, new_values_json as NewValuesJson,
                   diff_json as DiffJson, prev_hash as PrevHash, tamper_hash as TamperHash
            FROM audit_changelogs
            WHERE id = @Id;";

        return await connection.QuerySingleOrDefaultAsync<AuditChangeLog>(sql, new { Id = id });
    }

    public async Task<string?> GetLatestChangeLogHashAsync()
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = "SELECT tamper_hash FROM audit_changelogs ORDER BY timestamp DESC, id DESC LIMIT 1;";
        return await connection.QuerySingleOrDefaultAsync<string?>(sql);
    }
}
