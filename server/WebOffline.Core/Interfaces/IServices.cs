using System.Collections.Generic;
using System.Threading.Tasks;
using WebOffline.Core.Common;
using WebOffline.Core.DTOs;

namespace WebOffline.Core.Interfaces;

public interface IAuthService
{
    Task<ApiResponse<TokenResponse>> LoginAsync(LoginRequest request, string? clientIp, string? userAgent);
    Task<ApiResponse<UserDto>> RegisterAsync(RegisterRequest request, string currentUserId, bool isSystemAdmin);
    Task<ApiResponse<TokenResponse>> RefreshTokenAsync(RefreshTokenRequest request, string? clientIp);
    Task<ApiResponse> LogoutAsync(string? sessionId);
    Task<ApiResponse> RevokeSessionAsync(string sessionId, string currentUserId, bool isSystemAdmin);
    Task<ApiResponse> RevokeAllSessionsAsync(string currentUserId);
    Task<ApiResponse<IEnumerable<UserSessionDto>>> GetMySessionsAsync(string currentUserId, string? currentSessionId);
    Task<ApiResponse<UserDto>> GetMeAsync(string currentUserId);
    Task<ApiResponse<IEnumerable<UserDto>>> GetAllUsersAsync(bool isSystemAdmin);
}

public interface IWorkspaceService
{
    Task<ApiResponse<IEnumerable<WorkspaceDto>>> GetUserWorkspacesAsync(string currentUserId);
    Task<ApiResponse<WorkspaceDto>> GetWorkspaceByIdAsync(string workspaceId, string currentUserId, bool isSystemAdmin);
    Task<ApiResponse<WorkspaceDto>> CreateWorkspaceAsync(CreateWorkspaceRequest request, string currentUserId, string currentUserEmail);
    Task<ApiResponse<WorkspaceDto>> UpdateWorkspaceAsync(string workspaceId, UpdateWorkspaceRequest request, string currentUserId, string currentUserEmail, bool isSystemAdmin);
    Task<ApiResponse> DeleteWorkspaceAsync(string workspaceId, string currentUserId, string currentUserEmail, bool isSystemAdmin);
    Task<ApiResponse<IEnumerable<WorkspaceMemberDto>>> GetMembersAsync(string workspaceId, string currentUserId, bool isSystemAdmin);
    Task<ApiResponse> AddMemberAsync(string workspaceId, AddMemberRequest request, string currentUserId, string currentUserEmail, bool isSystemAdmin);
    Task<ApiResponse> RemoveMemberAsync(string workspaceId, string memberUserId, string currentUserId, string currentUserEmail, bool isSystemAdmin);
}

public interface IListService
{
    Task<ApiResponse<IEnumerable<ListDto>>> GetListsByWorkspaceAsync(string workspaceId, string currentUserId, bool isSystemAdmin);
    Task<ApiResponse<ListDto>> GetListByIdAsync(string listId, string currentUserId, bool isSystemAdmin);
    Task<ApiResponse<ListDto>> CreateListAsync(CreateListRequest request, string currentUserId, string currentUserEmail, bool isSystemAdmin);
    Task<ApiResponse<ListDto>> UpdateListAsync(string listId, UpdateListRequest request, string currentUserId, string currentUserEmail, bool isSystemAdmin);
    Task<ApiResponse> DeleteListAsync(string listId, string currentUserId, string currentUserEmail, bool isSystemAdmin);
}

public interface ITaskService
{
    Task<ApiResponse<IEnumerable<TaskDto>>> GetTasksByWorkspaceAsync(string workspaceId, string currentUserId, bool isSystemAdmin);
    Task<ApiResponse<IEnumerable<TaskDto>>> GetTasksByListAsync(string listId, string currentUserId, bool isSystemAdmin);
    Task<ApiResponse<TaskDto>> GetTaskByIdAsync(string taskId, string currentUserId, bool isSystemAdmin);
    Task<ApiResponse<TaskDto>> CreateTaskAsync(CreateTaskRequest request, string currentUserId, string currentUserEmail, bool isSystemAdmin);
    Task<ApiResponse<TaskDto>> UpdateTaskAsync(string taskId, UpdateTaskRequest request, string currentUserId, string currentUserEmail, bool isSystemAdmin);
    Task<ApiResponse<TaskDto>> UpdateTaskStatusAsync(string taskId, UpdateTaskStatusRequest request, string currentUserId, string currentUserEmail, bool isSystemAdmin);
    Task<ApiResponse> DeleteTaskAsync(string taskId, string currentUserId, string currentUserEmail, bool isSystemAdmin);
}
