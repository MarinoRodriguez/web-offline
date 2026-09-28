using System;
using System.Threading.Tasks;
using WebOffline.Core.Interfaces;

namespace WebOffline.Infrastructure.Services;

public class AbacPolicyEvaluator : IAbacPolicyEvaluator
{
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IListRepository _listRepository;
    private readonly ITaskRepository _taskRepository;

    public AbacPolicyEvaluator(
        IWorkspaceRepository workspaceRepository,
        IListRepository listRepository,
        ITaskRepository taskRepository)
    {
        _workspaceRepository = workspaceRepository;
        _listRepository = listRepository;
        _taskRepository = taskRepository;
    }

    public async Task<bool> CanAccessWorkspaceAsync(string userId, string workspaceId, ResourceAction action, bool isSystemAdmin = false)
    {
        if (isSystemAdmin) return true;

        var workspace = await _workspaceRepository.GetByIdAsync(workspaceId);
        if (workspace == null) return false;

        if (workspace.OwnerId == userId) return true;

        var role = await _workspaceRepository.GetUserRoleInWorkspaceAsync(workspaceId, userId);
        if (string.IsNullOrWhiteSpace(role)) return false;

        return action switch
        {
            ResourceAction.Read => true, // Viewer, Editor, Owner
            ResourceAction.Write => role.Equals("Owner", StringComparison.OrdinalIgnoreCase) ||
                                   role.Equals("Editor", StringComparison.OrdinalIgnoreCase),
            ResourceAction.Delete => role.Equals("Owner", StringComparison.OrdinalIgnoreCase),
            ResourceAction.Admin => role.Equals("Owner", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    public async Task<bool> CanAccessListAsync(string userId, string listId, ResourceAction action, bool isSystemAdmin = false)
    {
        if (isSystemAdmin) return true;

        var list = await _listRepository.GetByIdAsync(listId);
        if (list == null) return false;

        return await CanAccessWorkspaceAsync(userId, list.WorkspaceId, action, isSystemAdmin);
    }

    public async Task<bool> CanAccessTaskAsync(string userId, string taskId, ResourceAction action, bool isSystemAdmin = false)
    {
        if (isSystemAdmin) return true;

        var task = await _taskRepository.GetByIdAsync(taskId);
        if (task == null) return false;

        return await CanAccessWorkspaceAsync(userId, task.WorkspaceId, action, isSystemAdmin);
    }
}
