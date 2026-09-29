using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using WebOffline.Core.Entities;
using WebOffline.Core.Interfaces;
using WebOffline.Infrastructure.Data;

namespace WebOffline.Infrastructure.Repositories;

public class TaskRepository : ITaskRepository
{
    private readonly ISqliteDbConnectionFactory _connectionFactory;

    public TaskRepository(ISqliteDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<TaskItem?> GetByIdAsync(string id)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<TaskItem>(
            @"SELECT id, list_id as ListId, workspace_id as WorkspaceId, 
                     parent_task_id as ParentTaskId, title, description, 
                     status, priority, due_date as DueDate, position, 
                     created_by as CreatedBy, created_at as CreatedAt, 
                     updated_by as UpdatedBy, updated_at as UpdatedAt, 
                     version, is_deleted as IsDeleted
              FROM tasks 
              WHERE id = @Id AND is_deleted = 0;",
            new { Id = id });
    }

    public async Task<IEnumerable<TaskItem>> GetByWorkspaceIdAsync(string workspaceId)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<TaskItem>(
            @"SELECT id, list_id as ListId, workspace_id as WorkspaceId, 
                     parent_task_id as ParentTaskId, title, description, 
                     status, priority, due_date as DueDate, position, 
                     created_by as CreatedBy, created_at as CreatedAt, 
                     updated_by as UpdatedBy, updated_at as UpdatedAt, 
                     version, is_deleted as IsDeleted
              FROM tasks 
              WHERE workspace_id = @WorkspaceId AND is_deleted = 0
              ORDER BY position ASC, created_at ASC;",
            new { WorkspaceId = workspaceId });
    }

    public async Task<IEnumerable<TaskItem>> GetByListIdAsync(string listId)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<TaskItem>(
            @"SELECT id, list_id as ListId, workspace_id as WorkspaceId, 
                     parent_task_id as ParentTaskId, title, description, 
                     status, priority, due_date as DueDate, position, 
                     created_by as CreatedBy, created_at as CreatedAt, 
                     updated_by as UpdatedBy, updated_at as UpdatedAt, 
                     version, is_deleted as IsDeleted
              FROM tasks 
              WHERE list_id = @ListId AND is_deleted = 0
              ORDER BY position ASC, created_at ASC;",
            new { ListId = listId });
    }

    public async Task<IEnumerable<TaskItem>> GetSubtasksAsync(string parentTaskId)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<TaskItem>(
            @"SELECT id, list_id as ListId, workspace_id as WorkspaceId, 
                     parent_task_id as ParentTaskId, title, description, 
                     status, priority, due_date as DueDate, position, 
                     created_by as CreatedBy, created_at as CreatedAt, 
                     updated_by as UpdatedBy, updated_at as UpdatedAt, 
                     version, is_deleted as IsDeleted
              FROM tasks 
              WHERE parent_task_id = @ParentTaskId AND is_deleted = 0
              ORDER BY position ASC, created_at ASC;",
            new { ParentTaskId = parentTaskId });
    }

    public async Task<int> CreateAsync(TaskItem task)
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO tasks (id, list_id, workspace_id, parent_task_id, title, description, 
                               status, priority, due_date, position, created_by, created_at, 
                               updated_by, updated_at, version, is_deleted)
            VALUES (@Id, @ListId, @WorkspaceId, @ParentTaskId, @Title, @Description, 
                    @Status, @Priority, @DueDate, @Position, @CreatedBy, @CreatedAt, 
                    @UpdatedBy, @UpdatedAt, @Version, 0);";

        return await connection.ExecuteAsync(sql, new
        {
            task.Id,
            task.ListId,
            task.WorkspaceId,
            task.ParentTaskId,
            task.Title,
            task.Description,
            task.Status,
            task.Priority,
            DueDate = task.DueDate?.ToString("O"),
            task.Position,
            task.CreatedBy,
            CreatedAt = task.CreatedAt.ToString("O"),
            task.UpdatedBy,
            UpdatedAt = task.UpdatedAt.ToString("O"),
            task.Version
        });
    }

    public async Task<int> UpdateAsync(TaskItem task)
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = @"
            UPDATE tasks
            SET list_id = @ListId, title = @Title, description = @Description, status = @Status,
                priority = @Priority, due_date = @DueDate, position = @Position,
                updated_by = @UpdatedBy, updated_at = @UpdatedAt, version = version + 1
            WHERE id = @Id AND is_deleted = 0;";

        return await connection.ExecuteAsync(sql, new
        {
            task.Id,
            task.ListId,
            task.Title,
            task.Description,
            task.Status,
            task.Priority,
            DueDate = task.DueDate?.ToString("O"),
            task.Position,
            task.UpdatedBy,
            UpdatedAt = (task.UpdatedAt > DateTime.MinValue ? task.UpdatedAt : DateTime.UtcNow).ToString("O")
        });
    }

    public async Task<int> UpdateStatusAsync(string id, string status, long newVersion, string? updatedBy = null)
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = @"
            UPDATE tasks
            SET status = @Status, updated_by = @UpdatedBy, updated_at = @UpdatedAt, version = @Version
            WHERE id = @Id AND is_deleted = 0;";

        return await connection.ExecuteAsync(sql, new
        {
            Id = id,
            Status = status,
            UpdatedBy = updatedBy,
            UpdatedAt = DateTime.UtcNow.ToString("O"),
            Version = newVersion
        });
    }

    public async Task<int> SoftDeleteAsync(string id, string? updatedBy = null)
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = @"
            UPDATE tasks
            SET is_deleted = 1, updated_by = @UpdatedBy, updated_at = @UpdatedAt, version = version + 1
            WHERE (id = @Id OR parent_task_id = @Id) AND is_deleted = 0;";

        return await connection.ExecuteAsync(sql, new
        {
            Id = id,
            UpdatedBy = updatedBy,
            UpdatedAt = DateTime.UtcNow.ToString("O")
        });
    }

    public async Task<bool> AreAllSubtasksCompletedAsync(string parentTaskId)
    {
        using var connection = _connectionFactory.CreateConnection();
        var count = await connection.ExecuteScalarAsync<int>(
            @"SELECT COUNT(1) FROM tasks 
              WHERE parent_task_id = @ParentTaskId 
                AND is_deleted = 0 
                AND status != 'DONE';",
            new { ParentTaskId = parentTaskId });
        return count == 0;
    }

    public async Task<int> CountPendingSubtasksAsync(string parentTaskId)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(
            @"SELECT COUNT(1) FROM tasks 
              WHERE parent_task_id = @ParentTaskId 
                AND is_deleted = 0 
                AND status != 'DONE';",
            new { ParentTaskId = parentTaskId });
    }
}
