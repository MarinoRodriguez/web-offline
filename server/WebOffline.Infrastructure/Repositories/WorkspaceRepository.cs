using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using WebOffline.Core.DTOs;
using WebOffline.Core.Entities;
using WebOffline.Core.Interfaces;
using WebOffline.Infrastructure.Data;

namespace WebOffline.Infrastructure.Repositories;

public class WorkspaceRepository : IWorkspaceRepository
{
    private readonly ISqliteDbConnectionFactory _connectionFactory;

    public WorkspaceRepository(ISqliteDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Workspace?> GetByIdAsync(string id)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Workspace>(
            @"SELECT id, name, description, owner_id as OwnerId, created_by as CreatedBy,
                     created_at as CreatedAt, updated_by as UpdatedBy, updated_at as UpdatedAt, 
                     version, is_deleted as IsDeleted
              FROM workspaces 
              WHERE id = @Id AND is_deleted = 0;",
            new { Id = id });
    }

    public async Task<IEnumerable<WorkspaceDto>> GetUserWorkspacesAsync(string userId)
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = @"
            SELECT w.id, w.name, w.description, w.owner_id as OwnerId, w.created_by as CreatedBy,
                   w.created_at as CreatedAt, w.updated_by as UpdatedBy, w.updated_at as UpdatedAt, w.version,
                   COALESCE(wm.role, CASE WHEN w.owner_id = @UserId THEN 'Owner' ELSE 'Viewer' END) as RoleInWorkspace
            FROM workspaces w
            LEFT JOIN workspace_members wm ON w.id = wm.workspace_id AND wm.user_id = @UserId
            WHERE w.is_deleted = 0 
              AND (w.owner_id = @UserId OR wm.user_id = @UserId)
            ORDER BY w.name ASC;";

        return await connection.QueryAsync<WorkspaceDto>(sql, new { UserId = userId });
    }

    public async Task<int> CreateAsync(Workspace workspace)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var tx = connection.BeginTransaction();

        var wsSql = @"
            INSERT INTO workspaces (id, name, description, owner_id, created_by, created_at, updated_by, updated_at, version, is_deleted)
            VALUES (@Id, @Name, @Description, @OwnerId, @CreatedBy, @CreatedAt, @UpdatedBy, @UpdatedAt, @Version, 0);";

        var memberSql = @"
            INSERT INTO workspace_members (workspace_id, user_id, role, joined_at)
            VALUES (@WorkspaceId, @UserId, 'Owner', @JoinedAt);";

        var createdBy = string.IsNullOrWhiteSpace(workspace.CreatedBy) ? workspace.OwnerId : workspace.CreatedBy;

        var rows = await connection.ExecuteAsync(wsSql, new
        {
            workspace.Id,
            workspace.Name,
            workspace.Description,
            workspace.OwnerId,
            CreatedBy = createdBy,
            CreatedAt = workspace.CreatedAt.ToString("O"),
            workspace.UpdatedBy,
            UpdatedAt = workspace.UpdatedAt.ToString("O"),
            workspace.Version
        }, tx);

        await connection.ExecuteAsync(memberSql, new
        {
            WorkspaceId = workspace.Id,
            UserId = workspace.OwnerId,
            JoinedAt = workspace.CreatedAt.ToString("O")
        }, tx);

        tx.Commit();
        return rows;
    }

    public async Task<int> UpdateAsync(Workspace workspace)
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = @"
            UPDATE workspaces
            SET name = @Name, description = @Description, updated_by = @UpdatedBy, 
                updated_at = @UpdatedAt, version = version + 1
            WHERE id = @Id AND is_deleted = 0;";

        return await connection.ExecuteAsync(sql, new
        {
            workspace.Id,
            workspace.Name,
            workspace.Description,
            workspace.UpdatedBy,
            UpdatedAt = (workspace.UpdatedAt > DateTime.MinValue ? workspace.UpdatedAt : DateTime.UtcNow).ToString("O")
        });
    }

    public async Task<int> SoftDeleteAsync(string id, string? updatedBy = null)
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = @"
            UPDATE workspaces
            SET is_deleted = 1, updated_by = @UpdatedBy, updated_at = @UpdatedAt, version = version + 1
            WHERE id = @Id AND is_deleted = 0;";

        return await connection.ExecuteAsync(sql, new
        {
            Id = id,
            UpdatedBy = updatedBy,
            UpdatedAt = DateTime.UtcNow.ToString("O")
        });
    }

    public async Task<int> AddMemberAsync(WorkspaceMember member)
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO workspace_members (workspace_id, user_id, role, joined_at)
            VALUES (@WorkspaceId, @UserId, @Role, @JoinedAt)
            ON CONFLICT(workspace_id, user_id) DO UPDATE SET role = excluded.role;";

        return await connection.ExecuteAsync(sql, new
        {
            member.WorkspaceId,
            member.UserId,
            member.Role,
            JoinedAt = member.JoinedAt.ToString("O")
        });
    }

    public async Task<int> RemoveMemberAsync(string workspaceId, string userId)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(
            "DELETE FROM workspace_members WHERE workspace_id = @WorkspaceId AND user_id = @UserId;",
            new { WorkspaceId = workspaceId, UserId = userId });
    }

    public async Task<string?> GetUserRoleInWorkspaceAsync(string workspaceId, string userId)
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = @"
            SELECT role FROM workspace_members 
            WHERE workspace_id = @WorkspaceId AND user_id = @UserId;";
        return await connection.QuerySingleOrDefaultAsync<string?>(sql, new { WorkspaceId = workspaceId, UserId = userId });
    }

    public async Task<IEnumerable<WorkspaceMemberDto>> GetMembersAsync(string workspaceId)
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = @"
            SELECT wm.workspace_id as WorkspaceId, wm.user_id as UserId, 
                   u.email as Email, u.full_name as FullName, wm.role as Role, wm.joined_at as JoinedAt
            FROM workspace_members wm
            INNER JOIN users u ON wm.user_id = u.id
            WHERE wm.workspace_id = @WorkspaceId
            ORDER BY wm.joined_at ASC;";

        return await connection.QueryAsync<WorkspaceMemberDto>(sql, new { WorkspaceId = workspaceId });
    }
}
