using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using WebOffline.Core.Entities;
using WebOffline.Infrastructure.Services;

namespace WebOffline.Infrastructure.Data;

public class DbInitializer
{
    private readonly ISqliteDbConnectionFactory _connectionFactory;
    private readonly IPasswordHasher _passwordHasher;

    public DbInitializer(ISqliteDbConnectionFactory connectionFactory, IPasswordHasher passwordHasher)
    {
        _connectionFactory = connectionFactory;
        _passwordHasher = passwordHasher;
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
                list_id TEXT NOT NULL,
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
                FOREIGN KEY (list_id) REFERENCES lists(id) ON DELETE CASCADE,
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
        ";

        await connection.ExecuteAsync(sql);

        // Seed initial admin if none exists
        var adminCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM users WHERE system_role = 'admin';"
        );

        if (adminCount == 0)
        {
            var adminId = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow.ToString("O");
            var passwordHash = _passwordHasher.HashPassword("Admin123!");

            await connection.ExecuteAsync(@"
                INSERT INTO users (id, email, password_hash, full_name, system_role, created_at, updated_at, is_active)
                VALUES (@Id, @Email, @PasswordHash, @FullName, 'admin', @CreatedAt, @UpdatedAt, 1);
            ", new
            {
                Id = adminId,
                Email = "admin@offline.local",
                PasswordHash = passwordHash,
                FullName = "System Administrator",
                CreatedAt = now,
                UpdatedAt = now
            });
        }
    }
}
