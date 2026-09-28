using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WebOffline.Core.Common;
using WebOffline.Core.DTOs;
using WebOffline.Core.Entities;
using WebOffline.Core.Interfaces;

namespace WebOffline.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IUserSessionRepository _sessionRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IAuditService _auditService;

    public AuthService(
        IUserRepository userRepository,
        IUserSessionRepository sessionRepository,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IAuditService auditService)
    {
        _userRepository = userRepository;
        _sessionRepository = sessionRepository;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _auditService = auditService;
    }

    public async Task<ApiResponse<TokenResponse>> LoginAsync(LoginRequest request, string? clientIp, string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return ApiResponse<TokenResponse>.Fail("Email and password are required.", 400);
        }

        var user = await _userRepository.GetByEmailAsync(request.Email);
        if (user == null || !user.IsActive || !_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            return ApiResponse<TokenResponse>.Fail("Invalid email or password.", 401);
        }

        var sessionId = Guid.NewGuid().ToString();
        var refreshToken = _tokenService.GenerateRefreshToken();
        var refreshTokenHash = _tokenService.HashToken(refreshToken);

        var deviceInfo = !string.IsNullOrWhiteSpace(request.DeviceInfo) ? request.DeviceInfo : userAgent;

        var session = new UserSession
        {
            Id = sessionId,
            UserId = user.Id,
            RefreshTokenHash = refreshTokenHash,
            DeviceInfo = deviceInfo,
            IpAddress = clientIp,
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
                CreatedBy = user.CreatedBy,
                CreatedAt = user.CreatedAt,
                UpdatedBy = user.UpdatedBy,
                UpdatedAt = user.UpdatedAt
            }
        };

        return ApiResponse<TokenResponse>.Ok(response, "Login successful");
    }

    public async Task<ApiResponse<UserDto>> RegisterAsync(RegisterRequest request, string currentUserId, bool isSystemAdmin)
    {
        if (!isSystemAdmin)
        {
            return ApiResponse<UserDto>.Fail("Access denied: Only system administrators can register new users.", 403);
        }

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return ApiResponse<UserDto>.Fail("Email and password are required.", 400);
        }

        var existingUser = await _userRepository.GetByEmailAsync(request.Email);
        if (existingUser != null)
        {
            return ApiResponse<UserDto>.Fail("A user with this email already exists.", 409);
        }

        var systemRole = string.Equals(request.SystemRole, "admin", StringComparison.OrdinalIgnoreCase) ? "admin" : "user";

        var newUser = new User
        {
            Id = Guid.NewGuid().ToString(),
            Email = request.Email.Trim().ToLowerInvariant(),
            FullName = request.FullName.Trim(),
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            SystemRole = systemRole,
            CreatedBy = currentUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedBy = currentUserId,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true
        };

        await _userRepository.CreateAsync(newUser);

        // Audit changelog
        await _auditService.RecordChangeAsync(
            currentUserId,
            "admin-registration",
            "REGISTER_USER",
            "User",
            newUser.Id,
            $"Administrador {currentUserId} registró al nuevo usuario '{newUser.Email}' con rol '{newUser.SystemRole}'",
            (User?)null,
            newUser);

        var userDto = new UserDto
        {
            Id = newUser.Id,
            Email = newUser.Email,
            FullName = newUser.FullName,
            SystemRole = newUser.SystemRole,
            CreatedBy = newUser.CreatedBy,
            CreatedAt = newUser.CreatedAt,
            UpdatedBy = newUser.UpdatedBy,
            UpdatedAt = newUser.UpdatedAt
        };

        return ApiResponse<UserDto>.Ok(userDto, "User registered successfully", 201);
    }

    public async Task<ApiResponse<TokenResponse>> RefreshTokenAsync(RefreshTokenRequest request, string? clientIp)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return ApiResponse<TokenResponse>.Fail("Refresh token is required.", 400);
        }

        var incomingHash = _tokenService.HashToken(request.RefreshToken);
        var session = await _sessionRepository.GetByRefreshTokenHashAsync(incomingHash);

        if (session == null || session.IsRevoked || session.ExpiresAt <= DateTime.UtcNow)
        {
            return ApiResponse<TokenResponse>.Fail("Invalid, expired or revoked refresh token.", 401);
        }

        var user = await _userRepository.GetByIdAsync(session.UserId);
        if (user == null || !user.IsActive)
        {
            return ApiResponse<TokenResponse>.Fail("User account is inactive or not found.", 401);
        }

        // Revoke the old session to ensure Refresh Token Rotation
        await _sessionRepository.RevokeAsync(session.Id);

        var newSessionId = Guid.NewGuid().ToString();
        var newRefreshToken = _tokenService.GenerateRefreshToken();
        var newRefreshTokenHash = _tokenService.HashToken(newRefreshToken);

        var newSession = new UserSession
        {
            Id = newSessionId,
            UserId = user.Id,
            RefreshTokenHash = newRefreshTokenHash,
            DeviceInfo = session.DeviceInfo,
            IpAddress = clientIp,
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
                CreatedBy = user.CreatedBy,
                CreatedAt = user.CreatedAt,
                UpdatedBy = user.UpdatedBy,
                UpdatedAt = user.UpdatedAt
            }
        };

        return ApiResponse<TokenResponse>.Ok(response, "Token refreshed successfully");
    }

    public async Task<ApiResponse> LogoutAsync(string? sessionId)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            await _sessionRepository.RevokeAsync(sessionId);
        }

        return ApiResponse.Ok("Logged out successfully");
    }

    public async Task<ApiResponse> RevokeSessionAsync(string sessionId, string currentUserId, bool isSystemAdmin)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId);
        if (session == null)
        {
            return ApiResponse.Fail("Session not found.", 404);
        }

        if (session.UserId != currentUserId && !isSystemAdmin)
        {
            return ApiResponse.Fail("Access denied.", 403);
        }

        await _sessionRepository.RevokeAsync(sessionId);
        return ApiResponse.Ok("Session revoked successfully");
    }

    public async Task<ApiResponse> RevokeAllSessionsAsync(string currentUserId)
    {
        await _sessionRepository.RevokeAllForUserAsync(currentUserId);
        return ApiResponse.Ok("All active sessions revoked successfully");
    }

    public async Task<ApiResponse<IEnumerable<UserSessionDto>>> GetMySessionsAsync(string currentUserId, string? currentSessionId)
    {
        var sessions = await _sessionRepository.GetActiveSessionsForUserAsync(currentUserId);

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

        return ApiResponse<IEnumerable<UserSessionDto>>.Ok(dtos);
    }

    public async Task<ApiResponse<UserDto>> GetMeAsync(string currentUserId)
    {
        var user = await _userRepository.GetByIdAsync(currentUserId);
        if (user == null)
        {
            return ApiResponse<UserDto>.Fail("User not found.", 404);
        }

        var dto = new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            SystemRole = user.SystemRole,
            CreatedBy = user.CreatedBy,
            CreatedAt = user.CreatedAt,
            UpdatedBy = user.UpdatedBy,
            UpdatedAt = user.UpdatedAt
        };

        return ApiResponse<UserDto>.Ok(dto);
    }

    public async Task<ApiResponse<IEnumerable<UserDto>>> GetAllUsersAsync(bool isSystemAdmin)
    {
        if (!isSystemAdmin)
        {
            return ApiResponse<IEnumerable<UserDto>>.Fail("Access denied: System administrator only.", 403);
        }

        var users = await _userRepository.GetAllAsync();
        var dtos = users.Select(u => new UserDto
        {
            Id = u.Id,
            Email = u.Email,
            FullName = u.FullName,
            SystemRole = u.SystemRole,
            CreatedBy = u.CreatedBy,
            CreatedAt = u.CreatedAt,
            UpdatedBy = u.UpdatedBy,
            UpdatedAt = u.UpdatedAt
        });

        return ApiResponse<IEnumerable<UserDto>>.Ok(dtos);
    }
}
