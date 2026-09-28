using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebOffline.Core.Common;
using WebOffline.Core.DTOs;
using WebOffline.Core.Interfaces;

namespace WebOffline.Api.Controllers;

[Route("api/[controller]")]
public class AuthController : BaseApiController
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<TokenResponse>>> Login([FromBody] LoginRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        var result = await _authService.LoginAsync(request, ip, userAgent);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("register")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<UserDto>>> Register([FromBody] RegisterRequest request)
    {
        var result = await _authService.RegisterAsync(request, CurrentUserId, IsSystemAdmin);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<TokenResponse>>> Refresh([FromBody] RefreshTokenRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _authService.RefreshTokenAsync(request, ip);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<ActionResult<ApiResponse>> Logout()
    {
        var sessionId = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        var result = await _authService.LogoutAsync(sessionId);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("revoke-session/{sessionId}")]
    [Authorize]
    public async Task<ActionResult<ApiResponse>> RevokeSession(string sessionId)
    {
        var result = await _authService.RevokeSessionAsync(sessionId, CurrentUserId, IsSystemAdmin);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("revoke-all")]
    [Authorize]
    public async Task<ActionResult<ApiResponse>> RevokeAllSessions()
    {
        var result = await _authService.RevokeAllSessionsAsync(CurrentUserId);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("sessions")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<IEnumerable<UserSessionDto>>>> GetMySessions()
    {
        var currentSessionId = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        var result = await _authService.GetMySessionsAsync(CurrentUserId, currentSessionId);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetMe()
    {
        var result = await _authService.GetMeAsync(CurrentUserId);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("users")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<IEnumerable<UserDto>>>> GetAllUsers()
    {
        var result = await _authService.GetAllUsersAsync(IsSystemAdmin);
        return StatusCode(result.StatusCode, result);
    }
}
