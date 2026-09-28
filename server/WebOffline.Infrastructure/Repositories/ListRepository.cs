using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using WebOffline.Core.Entities;
using WebOffline.Core.Interfaces;
using WebOffline.Infrastructure.Data;

namespace WebOffline.Infrastructure.Repositories;

public class ListRepository : IListRepository
{
    private readonly ISqliteDbConnectionFactory _connectionFactory;

    public ListRepository(ISqliteDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<TaskList?> GetByIdAsync(string id)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<TaskList>(
            @"SELECT id, workspace_id as WorkspaceId, name, color, position, 
                     created_at as CreatedAt, updated_at as UpdatedAt, 
                     version, is_deleted as IsDeleted
              FROM lists 
              WHERE id = @Id AND is_deleted = 0;",
            new { Id = id });
    }

    public async Task<IEnumerable<TaskList>> GetByWorkspaceIdAsync(string workspaceId)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<TaskList>(
            @"SELECT id, workspace_id as WorkspaceId, name, color, position, 
                     created_at as CreatedAt, updated_at as UpdatedAt, 
                     version, is_deleted as IsDeleted
              FROM lists 
              WHERE workspace_id = @WorkspaceId AND is_deleted = 0
              ORDER BY position ASC, created_at ASC;",
            new { WorkspaceId = workspaceId });
    }

    public async Task<int> CreateAsync(TaskList list)
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO lists (id, workspace_id, name, color, position, created_at, updated_at, version, is_deleted)
            VALUES (@Id, @WorkspaceId, @Name, @Color, @Position, @CreatedAt, @UpdatedAt, @Version, 0);";

        return await connection.ExecuteAsync(sql, new
        {
            list.Id,
            list.WorkspaceId,
            list.Name,
            list.Color,
            list.Position,
            CreatedAt = list.CreatedAt.ToString("O"),
            UpdatedAt = list.UpdatedAt.ToString("O"),
            list.Version
        });
    }

    public async Task<int> UpdateAsync(TaskList list)
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = @"
            UPDATE lists
            SET name = @Name, color = @Color, position = @Position, updated_at = @UpdatedAt, version = version + 1
            WHERE id = @Id AND is_deleted = 0;";

        return await connection.ExecuteAsync(sql, new
        {
            list.Id,
            list.Name,
            list.Color,
            list.Position,
            UpdatedAt = DateTime.UtcNow.ToString("O")
        });
    }

    public async Task<int> SoftDeleteAsync(string id)
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = @"
            UPDATE lists
            SET is_deleted = 1, updated_at = @UpdatedAt, version = version + 1
            WHERE id = @Id AND is_deleted = 0;";

        return await connection.ExecuteAsync(sql, new
        {
            Id = id,
            UpdatedAt = DateTime.UtcNow.ToString("O")
        });
    }
}
