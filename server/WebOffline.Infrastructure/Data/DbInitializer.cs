using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Configuration;
using WebOffline.Core.Entities;
using WebOffline.Infrastructure.Services;

namespace WebOffline.Infrastructure.Data;

public class DbInitializer
{
    private readonly ISqliteDbConnectionFactory _connectionFactory;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IConfiguration? _configuration;

    public DbInitializer(
        ISqliteDbConnectionFactory connectionFactory,
        IPasswordHasher passwordHasher,
        IConfiguration? configuration = null)
    {
        _connectionFactory = connectionFactory;
        _passwordHasher = passwordHasher;
        _configuration = configuration;
    }

    public async Task InitializeAsync()
    {
        using var connection = _connectionFactory.CreateConnection();

        // Enable foreign keys and WAL mode for high concurrency SQLite performance
        await connection.ExecuteAsync("PRAGMA foreign_keys = ON;");
        await connection.ExecuteAsync("PRAGMA journal_mode = WAL;");

        var sql = @"
            CREATE TABLE IF NOT EXISTS users (
                id TEXT PRIMARY KEY,
                email TEXT UNIQUE NOT NULL,
                password_hash TEXT NOT NULL,
                full_name TEXT NOT NULL,
                system_role TEXT NOT NULL DEFAULT 'user',
                created_by TEXT,
                created_at TEXT NOT NULL,
                updated_by TEXT,
                updated_at TEXT NOT NULL,
                is_active INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS user_sessions (
                id TEXT PRIMARY KEY,
                user_id TEXT NOT NULL,
                refresh_token_hash TEXT NOT NULL,
                device_info TEXT,
                ip_address TEXT,
                expires_at TEXT NOT NULL,
                created_at TEXT NOT NULL,
                revoked_at TEXT,
                is_revoked INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS workspaces (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                description TEXT,
                owner_id TEXT NOT NULL,
                created_by TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_by TEXT,
                updated_at TEXT NOT NULL,
                version INTEGER NOT NULL DEFAULT 1,
                is_deleted INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (owner_id) REFERENCES users(id)
            );

            CREATE TABLE IF NOT EXISTS workspace_members (
                workspace_id TEXT NOT NULL,
                user_id TEXT NOT NULL,
                role TEXT NOT NULL DEFAULT 'Editor',
                joined_at TEXT NOT NULL,
                PRIMARY KEY (workspace_id, user_id),
                FOREIGN KEY (workspace_id) REFERENCES workspaces(id) ON DELETE CASCADE,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS lists (
                id TEXT PRIMARY KEY,
                workspace_id TEXT NOT NULL,
                name TEXT NOT NULL,
                color TEXT NOT NULL DEFAULT '#3B82F6',
                position INTEGER NOT NULL DEFAULT 0,
                created_by TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_by TEXT,
                updated_at TEXT NOT NULL,
                version INTEGER NOT NULL DEFAULT 1,
                is_deleted INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (workspace_id) REFERENCES workspaces(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS tasks (
                id TEXT PRIMARY KEY,
                list_id TEXT,
                workspace_id TEXT NOT NULL,
                parent_task_id TEXT,
                title TEXT NOT NULL,
                description TEXT,
                status TEXT NOT NULL DEFAULT 'TODO',
                priority TEXT NOT NULL DEFAULT 'MEDIUM',
                due_date TEXT,
                position INTEGER NOT NULL DEFAULT 0,
                created_by TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_by TEXT,
                updated_at TEXT NOT NULL,
                version INTEGER NOT NULL DEFAULT 1,
                is_deleted INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (list_id) REFERENCES lists(id) ON DELETE SET NULL,
                FOREIGN KEY (workspace_id) REFERENCES workspaces(id) ON DELETE CASCADE,
                FOREIGN KEY (parent_task_id) REFERENCES tasks(id) ON DELETE CASCADE,
                FOREIGN KEY (created_by) REFERENCES users(id)
            );

            CREATE INDEX IF NOT EXISTS idx_users_email ON users(email);
            CREATE INDEX IF NOT EXISTS idx_sessions_user_id ON user_sessions(user_id);
            CREATE INDEX IF NOT EXISTS idx_sessions_refresh ON user_sessions(refresh_token_hash);
            CREATE INDEX IF NOT EXISTS idx_workspaces_updated ON workspaces(updated_at);
            CREATE INDEX IF NOT EXISTS idx_lists_workspace ON lists(workspace_id);
            CREATE INDEX IF NOT EXISTS idx_lists_updated ON lists(updated_at);
            CREATE INDEX IF NOT EXISTS idx_tasks_list ON tasks(list_id);
            CREATE INDEX IF NOT EXISTS idx_tasks_workspace ON tasks(workspace_id);
            CREATE INDEX IF NOT EXISTS idx_tasks_parent ON tasks(parent_task_id);
            CREATE INDEX IF NOT EXISTS idx_tasks_updated ON tasks(updated_at);

            CREATE TABLE IF NOT EXISTS sync_tombstones (
                id TEXT PRIMARY KEY,
                entity_type TEXT NOT NULL,
                entity_id TEXT NOT NULL,
                workspace_id TEXT,
                deleted_by TEXT NOT NULL,
                deleted_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_tombstones_deleted ON sync_tombstones(deleted_at);
            CREATE INDEX IF NOT EXISTS idx_tombstones_workspace ON sync_tombstones(workspace_id);
        ";

        await connection.ExecuteAsync(sql);

        // Migration check: Ensure list_id is nullable in tasks table
        var tableInfo = await connection.QueryAsync("PRAGMA table_info(tasks);");
        var listIdColumn = tableInfo.FirstOrDefault(c => (string)c.name == "list_id");
        if (listIdColumn != null && (long)listIdColumn.notnull == 1)
        {
            await connection.ExecuteAsync(@"
                PRAGMA foreign_keys = OFF;
                ALTER TABLE tasks RENAME TO _tasks_old;
                CREATE TABLE tasks (
                    id TEXT PRIMARY KEY,
                    list_id TEXT,
                    workspace_id TEXT NOT NULL,
                    parent_task_id TEXT,
                    title TEXT NOT NULL,
                    description TEXT,
                    status TEXT NOT NULL DEFAULT 'TODO',
                    priority TEXT NOT NULL DEFAULT 'MEDIUM',
                    due_date TEXT,
                    position INTEGER NOT NULL DEFAULT 0,
                    created_by TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_by TEXT,
                    updated_at TEXT NOT NULL,
                    version INTEGER NOT NULL DEFAULT 1,
                    is_deleted INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY (list_id) REFERENCES lists(id) ON DELETE SET NULL,
                    FOREIGN KEY (workspace_id) REFERENCES workspaces(id) ON DELETE CASCADE,
                    FOREIGN KEY (parent_task_id) REFERENCES tasks(id) ON DELETE CASCADE,
                    FOREIGN KEY (created_by) REFERENCES users(id)
                );
                INSERT INTO tasks SELECT * FROM _tasks_old;
                DROP TABLE _tasks_old;
                PRAGMA foreign_keys = ON;
            ");
        }

        // Seed initial admin if none exists
        var adminCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM users WHERE system_role = 'admin';"
        );

        if (adminCount == 0)
        {
            var adminId = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow.ToString("O");
            var adminEmail = _configuration?["InitialAdmin:Email"] ?? "admin@offline.local";
            var adminPassword = _configuration?["InitialAdmin:Password"] ?? "Admin123!";
            var adminFullName = _configuration?["InitialAdmin:FullName"] ?? "System Administrator";
            var passwordHash = _passwordHasher.HashPassword(adminPassword);

            await connection.ExecuteAsync(@"
                INSERT INTO users (id, email, password_hash, full_name, system_role, created_at, updated_at, is_active)
                VALUES (@Id, @Email, @PasswordHash, @FullName, 'admin', @CreatedAt, @UpdatedAt, 1);
            ", new
            {
                Id = adminId,
                Email = adminEmail,
                PasswordHash = passwordHash,
                FullName = adminFullName,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
    }
}
