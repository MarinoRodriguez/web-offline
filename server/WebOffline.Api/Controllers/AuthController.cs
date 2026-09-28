using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WebOffline.Api.Services;
using WebOffline.Core.Common;
using WebOffline.Core.DTOs;
using WebOffline.Core.Entities;
using WebOffline.Core.Interfaces;
using WebOffline.Infrastructure.Services;

namespace WebOffline.Api.Controllers;

[Route("api/[controller]")]
public class AuthController : BaseApiController
{
    private readonly IUserRepository _userRepository;
    private readonly IUserSessionRepository _sessionRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;

    public AuthController(
        IUserRepository userRepository,
        IUserSessionRepository sessionRepository,
        IPasswordHasher passwordHasher,
        ITokenService tokenService)
    {
        _userRepository = userRepository;
        _sessionRepository = sessionRepository;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<TokenResponse>>> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(ApiResponse<TokenResponse>.Fail("Email and password are required."));
        }

        var user = await _userRepository.GetByEmailAsync(request.Email);
        if (user == null || !user.IsActive || !_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            return Unauthorized(ApiResponse<TokenResponse>.Fail("Invalid email or password.", 401));
        }

        var sessionId = Guid.NewGuid().ToString();
        var refreshToken = _tokenService.GenerateRefreshToken();
        var refreshTokenHash = _tokenService.HashToken(refreshToken);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        var deviceInfo = !string.IsNullOrWhiteSpace(request.DeviceInfo) ? request.DeviceInfo : userAgent;

        var session = new UserSession
        {
            Id = sessionId,
            UserId = user.Id,
            RefreshTokenHash = refreshTokenHash,
            DeviceInfo = deviceInfo,
            IpAddress = ip,
            ExpiresAt = DateTime.UtcNow.AddDays(14),
            CreatedAt = DateTime.UtcNow,
            IsRevoked = false
        };

        await _sessionRepository.CreateAsync(session);

        var (accessToken, accessExpiresAt) = _tokenService.GenerateAccessToken(user, sessionId);

        var response = new TokenResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = accessExpiresAt,
            SessionId = sessionId,
            User = new UserDto
            {
                Id = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                SystemRole = user.SystemRole,
                CreatedAt = user.CreatedAt
            }
        };

        return Ok(ApiResponse<TokenResponse>.Ok(response, "Login successful"));
    }

    [HttpPost("register")]
    [Authorize] // Only authenticated admins can register new users
    public async Task<ActionResult<ApiResponse<UserDto>>> Register([FromBody] RegisterRequest request)
    {
        // Enforce ABAC / Admin only check
        if (!IsSystemAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden, 
                ApiResponse<UserDto>.Fail("Access denied: Only system administrators can register new users.", 403));
        }

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(ApiResponse<UserDto>.Fail("Email and password are required."));
        }

        var existingUser = await _userRepository.GetByEmailAsync(request.Email);
        if (existingUser != null)
        {
            return Conflict(ApiResponse<UserDto>.Fail("A user with this email already exists.", 409));
        }

        var systemRole = string.Equals(request.SystemRole, "admin", StringComparison.OrdinalIgnoreCase) ? "admin" : "user";

        var newUser = new User
        {
            Id = Guid.NewGuid().ToString(),
            Email = request.Email.Trim().ToLowerInvariant(),
            FullName = request.FullName.Trim(),
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            SystemRole = systemRole,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true
        };

        await _userRepository.CreateAsync(newUser);

        var userDto = new UserDto
        {
            Id = newUser.Id,
            Email = newUser.Email,
            FullName = newUser.FullName,
            SystemRole = newUser.SystemRole,
            CreatedAt = newUser.CreatedAt
        };

        return CreatedAtAction(nameof(GetMe), null, ApiResponse<UserDto>.Ok(userDto, "User registered successfully", 201));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<TokenResponse>>> Refresh([FromBody] RefreshTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return BadRequest(ApiResponse<TokenResponse>.Fail("Refresh token is required."));
        }

        var incomingHash = _tokenService.HashToken(request.RefreshToken);
        var session = await _sessionRepository.GetByRefreshTokenHashAsync(incomingHash);

        if (session == null || session.IsRevoked || session.ExpiresAt <= DateTime.UtcNow)
        {
            return Unauthorized(ApiResponse<TokenResponse>.Fail("Invalid, expired or revoked refresh token.", 401));
        }

        var user = await _userRepository.GetByIdAsync(session.UserId);
        if (user == null || !user.IsActive)
        {
            return Unauthorized(ApiResponse<TokenResponse>.Fail("User account is inactive or not found.", 401));
        }

        // Revoke the old session to ensure Refresh Token Rotation
        await _sessionRepository.RevokeAsync(session.Id);

        // Create a new session
        var newSessionId = Guid.NewGuid().ToString();
        var newRefreshToken = _tokenService.GenerateRefreshToken();
        var newRefreshTokenHash = _tokenService.HashToken(newRefreshToken);

        var newSession = new UserSession
        {
            Id = newSessionId,
            UserId = user.Id,
            RefreshTokenHash = newRefreshTokenHash,
            DeviceInfo = session.DeviceInfo,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            ExpiresAt = DateTime.UtcNow.AddDays(14),
            CreatedAt = DateTime.UtcNow,
            IsRevoked = false
        };

        await _sessionRepository.CreateAsync(newSession);

        var (newAccessToken, accessExpiresAt) = _tokenService.GenerateAccessToken(user, newSessionId);

        var response = new TokenResponse
        {
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken,
            ExpiresAt = accessExpiresAt,
            SessionId = newSessionId,
            User = new UserDto
            {
                Id = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                SystemRole = user.SystemRole,
                CreatedAt = user.CreatedAt
            }
        };

        return Ok(ApiResponse<TokenResponse>.Ok(response, "Token refreshed successfully"));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<ActionResult<ApiResponse>> Logout()
    {
        var sessionId = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            await _sessionRepository.RevokeAsync(sessionId);
        }

        return Ok(ApiResponse.Ok("Logged out successfully"));
    }

    [HttpPost("revoke-session/{sessionId}")]
    [Authorize]
    public async Task<ActionResult<ApiResponse>> RevokeSession(string sessionId)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId);
        if (session == null)
        {
            return NotFound(ApiResponse.Fail("Session not found.", 404));
        }

        // Only the session owner or a system admin can revoke it
        if (session.UserId != CurrentUserId && !IsSystemAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail("Access denied.", 403));
        }

        await _sessionRepository.RevokeAsync(sessionId);
        return Ok(ApiResponse.Ok("Session revoked successfully"));
    }

    [HttpPost("revoke-all")]
    [Authorize]
    public async Task<ActionResult<ApiResponse>> RevokeAllSessions()
    {
        await _sessionRepository.RevokeAllForUserAsync(CurrentUserId);
        return Ok(ApiResponse.Ok("All active sessions revoked successfully"));
    }

    [HttpGet("sessions")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<IEnumerable<UserSessionDto>>>> GetMySessions()
    {
        var currentSessionId = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        var sessions = await _sessionRepository.GetActiveSessionsForUserAsync(CurrentUserId);

        var dtos = sessions.Select(s => new UserSessionDto
        {
            Id = s.Id,
            DeviceInfo = s.DeviceInfo,
            IpAddress = s.IpAddress,
            CreatedAt = s.CreatedAt,
            ExpiresAt = s.ExpiresAt,
            IsActive = s.IsActiveSession,
            IsCurrent = s.Id == currentSessionId
        });

        return Ok(ApiResponse<IEnumerable<UserSessionDto>>.Ok(dtos));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetMe()
    {
        var user = await _userRepository.GetByIdAsync(CurrentUserId);
        if (user == null)
        {
            return NotFound(ApiResponse<UserDto>.Fail("User not found.", 404));
        }

        var dto = new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            SystemRole = user.SystemRole,
            CreatedAt = user.CreatedAt
        };

        return Ok(ApiResponse<UserDto>.Ok(dto));
    }

    [HttpGet("users")]
    [Authorize] // Only admins can list users for management/ABAC
    public async Task<ActionResult<ApiResponse<IEnumerable<UserDto>>>> GetAllUsers()
    {
        if (!IsSystemAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden, 
                ApiResponse<IEnumerable<UserDto>>.Fail("Access denied: System administrator only.", 403));
        }

        var users = await _userRepository.GetAllAsync();
        var dtos = users.Select(u => new UserDto
        {
            Id = u.Id,
            Email = u.Email,
            FullName = u.FullName,
            SystemRole = u.SystemRole,
            CreatedAt = u.CreatedAt
        });

        return Ok(ApiResponse<IEnumerable<UserDto>>.Ok(dtos));
    }
}
