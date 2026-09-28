using System.Data;
using System.Threading.Tasks;
using Dapper;

namespace WebOffline.Infrastructure.Data;

public class AuditDbInitializer
{
    private readonly IAuditDbConnectionFactory _connectionFactory;

    public AuditDbInitializer(IAuditDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task InitializeAsync()
    {
        using var connection = _connectionFactory.CreateConnection();

        await connection.ExecuteAsync("PRAGMA journal_mode = WAL;");

        var sql = @"
            CREATE TABLE IF NOT EXISTS http_request_logs (
                id TEXT PRIMARY KEY,
                correlation_id TEXT NOT NULL,
                user_id TEXT,
                user_email TEXT,
                client_ip TEXT,
                user_agent TEXT,
                http_method TEXT NOT NULL,
                path TEXT NOT NULL,
                query_string TEXT,
                request_body TEXT,
                status_code INTEGER NOT NULL,
                response_body TEXT,
                duration_ms INTEGER NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS audit_changelogs (
                id TEXT PRIMARY KEY,
                timestamp TEXT NOT NULL,
                user_id TEXT NOT NULL,
                user_email TEXT NOT NULL,
                action TEXT NOT NULL,
                entity_type TEXT NOT NULL,
                entity_id TEXT NOT NULL,
                description TEXT NOT NULL,
                old_values_json TEXT,
                new_values_json TEXT,
                diff_json TEXT,
                prev_hash TEXT,
                tamper_hash TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_httplogs_created ON http_request_logs(created_at);
            CREATE INDEX IF NOT EXISTS idx_httplogs_user ON http_request_logs(user_id);
            CREATE INDEX IF NOT EXISTS idx_httplogs_path ON http_request_logs(path);

            CREATE INDEX IF NOT EXISTS idx_changelog_timestamp ON audit_changelogs(timestamp);
            CREATE INDEX IF NOT EXISTS idx_changelog_entity ON audit_changelogs(entity_type, entity_id);
            CREATE INDEX IF NOT EXISTS idx_changelog_user ON audit_changelogs(user_id);
        ";

        await connection.ExecuteAsync(sql);
    }
}
