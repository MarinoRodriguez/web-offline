using System.Threading.Tasks;

namespace WebOffline.Core.Interfaces;

public enum ResourceAction
{
    Read,
    Write,
    Delete,
    Admin
}

public interface IAbacPolicyEvaluator
{
    Task<bool> CanAccessWorkspaceAsync(string userId, string workspaceId, ResourceAction action, bool isSystemAdmin = false);
    Task<bool> CanAccessListAsync(string userId, string listId, ResourceAction action, bool isSystemAdmin = false);
    Task<bool> CanAccessTaskAsync(string userId, string taskId, ResourceAction action, bool isSystemAdmin = false);
}
