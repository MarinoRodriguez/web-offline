using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace WebOffline.Api.Controllers;

[ApiController]
public abstract class BaseApiController : ControllerBase
{
    protected string CurrentUserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
    protected string CurrentUserEmail => User.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
    protected string CurrentUserRole => User.FindFirst(ClaimTypes.Role)?.Value ?? "user";
    protected bool IsSystemAdmin => CurrentUserRole.Equals("admin", System.StringComparison.OrdinalIgnoreCase);
}
