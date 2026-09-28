using System.Collections.Generic;
using System.Threading.Tasks;
using WebOffline.Core.DTOs;
using WebOffline.Core.Entities;

namespace WebOffline.Core.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(string id);
    Task<User?> GetByEmailAsync(string email);
    Task<IEnumerable<User>> GetAllAsync();
    Task<int> CreateAsync(User user);
    Task<int> UpdateAsync(User user);
    Task<bool> HasAnyAdminAsync();
    Task<int> CountAsync();
}

public interface IUserSessionRepository
{
    Task<int> CreateAsync(UserSession session);
    Task<UserSession?> GetByIdAsync(string sessionId);
    Task<UserSession?> GetByRefreshTokenHashAsync(string refreshTokenHash);
    Task<int> RevokeAsync(string sessionId);
    Task<int> RevokeAllForUserAsync(string userId);
    Task<IEnumerable<UserSession>> GetActiveSessionsForUserAsync(string userId);
}

public interface IWorkspaceRepository
{
    Task<Workspace?> GetByIdAsync(string id);
    Task<IEnumerable<WorkspaceDto>> GetUserWorkspacesAsync(string userId);
    Task<int> CreateAsync(Workspace workspace);
    Task<int> UpdateAsync(Workspace workspace);
    Task<int> SoftDeleteAsync(string id);
    Task<int> AddMemberAsync(WorkspaceMember member);
    Task<int> RemoveMemberAsync(string workspaceId, string userId);
    Task<string?> GetUserRoleInWorkspaceAsync(string workspaceId, string userId);
    Task<IEnumerable<WorkspaceMemberDto>> GetMembersAsync(string workspaceId);
}

public interface IListRepository
{
    Task<TaskList?> GetByIdAsync(string id);
    Task<IEnumerable<TaskList>> GetByWorkspaceIdAsync(string workspaceId);
    Task<int> CreateAsync(TaskList list);
    Task<int> UpdateAsync(TaskList list);
    Task<int> SoftDeleteAsync(string id);
}

public interface ITaskRepository
{
    Task<TaskItem?> GetByIdAsync(string id);
    Task<IEnumerable<TaskItem>> GetByWorkspaceIdAsync(string workspaceId);
    Task<IEnumerable<TaskItem>> GetByListIdAsync(string listId);
    Task<IEnumerable<TaskItem>> GetSubtasksAsync(string parentTaskId);
    Task<int> CreateAsync(TaskItem task);
    Task<int> UpdateAsync(TaskItem task);
    Task<int> UpdateStatusAsync(string id, string status, long newVersion);
    Task<int> SoftDeleteAsync(string id);
    Task<bool> AreAllSubtasksCompletedAsync(string parentTaskId);
    Task<int> CountPendingSubtasksAsync(string parentTaskId);
}
