using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using WebOffline.Core.Entities;
using WebOffline.Core.Interfaces;
using WebOffline.Infrastructure.Data;

namespace WebOffline.Infrastructure.Repositories;

public class SyncRepository : ISyncRepository
{
    private readonly ISqliteDbConnectionFactory _connectionFactory;

    public SyncRepository(ISqliteDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<int> RecordTombstoneAsync(SyncTombstone tombstone)
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO sync_tombstones (id, entity_type, entity_id, workspace_id, deleted_by, deleted_at)
            VALUES (@Id, @EntityType, @EntityId, @WorkspaceId, @DeletedBy, @DeletedAt);
        ";
        return await connection.ExecuteAsync(sql, new
        {
            tombstone.Id,
            tombstone.EntityType,
            tombstone.EntityId,
            tombstone.WorkspaceId,
            tombstone.DeletedBy,
            DeletedAt = tombstone.DeletedAt.ToString("O")
        });
    }

    public async Task<IEnumerable<SyncTombstone>> GetTombstonesSinceAsync(DateTime? sinceUtc, IEnumerable<string> workspaceIds)
    {
        var wsList = workspaceIds.ToList();
        if (!wsList.Any())
        {
            return Enumerable.Empty<SyncTombstone>();
        }

        using var connection = _connectionFactory.CreateConnection();
        var sinceIso = sinceUtc?.ToString("O");

        var sql = @"
            SELECT id, entity_type as EntityType, entity_id as EntityId, workspace_id as WorkspaceId,
                   deleted_by as DeletedBy, deleted_at as DeletedAt
            FROM sync_tombstones
            WHERE (@SinceIso IS NULL OR deleted_at > @SinceIso)
              AND (workspace_id IN @WorkspaceIds OR workspace_id IS NULL)
            ORDER BY deleted_at ASC;
        ";

        return await connection.QueryAsync<SyncTombstone>(sql, new { SinceIso = sinceIso, WorkspaceIds = wsList });
    }

    public async Task<IEnumerable<Workspace>> GetWorkspacesUpdatedSinceAsync(DateTime? sinceUtc, IEnumerable<string> workspaceIds)
    {
        var wsList = workspaceIds.ToList();
        if (!wsList.Any())
        {
            return Enumerable.Empty<Workspace>();
        }

        using var connection = _connectionFactory.CreateConnection();
        var sinceIso = sinceUtc?.ToString("O");

        var sql = @"
            SELECT id, name, description, owner_id as OwnerId, created_by as CreatedBy,
                   created_at as CreatedAt, updated_by as UpdatedBy, updated_at as UpdatedAt,
                   version, is_deleted as IsDeleted
            FROM workspaces
            WHERE id IN @WorkspaceIds
              AND (@SinceIso IS NULL OR updated_at > @SinceIso)
            ORDER BY updated_at ASC;
        ";

        return await connection.QueryAsync<Workspace>(sql, new { SinceIso = sinceIso, WorkspaceIds = wsList });
    }

    public async Task<IEnumerable<TaskList>> GetListsUpdatedSinceAsync(DateTime? sinceUtc, IEnumerable<string> workspaceIds)
    {
        var wsList = workspaceIds.ToList();
        if (!wsList.Any())
        {
            return Enumerable.Empty<TaskList>();
        }

        using var connection = _connectionFactory.CreateConnection();
        var sinceIso = sinceUtc?.ToString("O");

        var sql = @"
            SELECT id, workspace_id as WorkspaceId, name, color, position,
                   created_by as CreatedBy, created_at as CreatedAt,
                   updated_by as UpdatedBy, updated_at as UpdatedAt,
                   version, is_deleted as IsDeleted
            FROM lists
            WHERE workspace_id IN @WorkspaceIds
              AND (@SinceIso IS NULL OR updated_at > @SinceIso)
            ORDER BY updated_at ASC;
        ";

        return await connection.QueryAsync<TaskList>(sql, new { SinceIso = sinceIso, WorkspaceIds = wsList });
    }

    public async Task<IEnumerable<TaskItem>> GetTasksUpdatedSinceAsync(DateTime? sinceUtc, IEnumerable<string> workspaceIds)
    {
        var wsList = workspaceIds.ToList();
        if (!wsList.Any())
        {
            return Enumerable.Empty<TaskItem>();
        }

        using var connection = _connectionFactory.CreateConnection();
        var sinceIso = sinceUtc?.ToString("O");

        var sql = @"
            SELECT id, list_id as ListId, workspace_id as WorkspaceId, parent_task_id as ParentTaskId,
                   title, description, status, priority, due_date as DueDate, position,
                   created_by as CreatedBy, created_at as CreatedAt,
                   updated_by as UpdatedBy, updated_at as UpdatedAt,
                   version, is_deleted as IsDeleted
            FROM tasks
            WHERE workspace_id IN @WorkspaceIds
              AND (@SinceIso IS NULL OR updated_at > @SinceIso)
            ORDER BY updated_at ASC;
        ";

        return await connection.QueryAsync<TaskItem>(sql, new { SinceIso = sinceIso, WorkspaceIds = wsList });
    }

    public async Task<IEnumerable<string>> GetAccessibleWorkspaceIdsAsync(string userId, bool isSystemAdmin)
    {
        using var connection = _connectionFactory.CreateConnection();

        if (isSystemAdmin)
        {
            return await connection.QueryAsync<string>("SELECT id FROM workspaces WHERE is_deleted = 0;");
        }

        var sql = @"
            SELECT DISTINCT id FROM workspaces 
            WHERE is_deleted = 0 
              AND (owner_id = @UserId OR id IN (SELECT workspace_id FROM workspace_members WHERE user_id = @UserId));
        ";

        return await connection.QueryAsync<string>(sql, new { UserId = userId });
    }
}
