using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using WebOffline.Core.Entities;
using WebOffline.Core.Interfaces;
using WebOffline.Infrastructure.Data;

namespace WebOffline.Infrastructure.Repositories;

public class UserSessionRepository : IUserSessionRepository
{
    private readonly ISqliteDbConnectionFactory _connectionFactory;

    public UserSessionRepository(ISqliteDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<int> CreateAsync(UserSession session)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(
            @"INSERT INTO user_sessions (id, user_id, refresh_token_hash, device_info, ip_address, expires_at, created_at, revoked_at, is_revoked)
              VALUES (@Id, @UserId, @RefreshTokenHash, @DeviceInfo, @IpAddress, @ExpiresAt, @CreatedAt, @RevokedAt, @IsRevoked);",
            new
            {
                session.Id,
                session.UserId,
                session.RefreshTokenHash,
                session.DeviceInfo,
                session.IpAddress,
                ExpiresAt = session.ExpiresAt.ToString("O"),
                CreatedAt = session.CreatedAt.ToString("O"),
                RevokedAt = session.RevokedAt?.ToString("O"),
                IsRevoked = session.IsRevoked ? 1 : 0
            });
    }

    public async Task<UserSession?> GetByIdAsync(string sessionId)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<UserSession>(
            @"SELECT id, user_id as UserId, refresh_token_hash as RefreshTokenHash, device_info as DeviceInfo,
                     ip_address as IpAddress, expires_at as ExpiresAt, created_at as CreatedAt,
                     revoked_at as RevokedAt, is_revoked as IsRevoked
              FROM user_sessions WHERE id = @Id;",
            new { Id = sessionId });
    }

    public async Task<UserSession?> GetByRefreshTokenHashAsync(string refreshTokenHash)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<UserSession>(
            @"SELECT id, user_id as UserId, refresh_token_hash as RefreshTokenHash, device_info as DeviceInfo,
                     ip_address as IpAddress, expires_at as ExpiresAt, created_at as CreatedAt,
                     revoked_at as RevokedAt, is_revoked as IsRevoked
              FROM user_sessions WHERE refresh_token_hash = @Hash;",
            new { Hash = refreshTokenHash });
    }

    public async Task<int> RevokeAsync(string sessionId)
    {
        using var connection = _connectionFactory.CreateConnection();
        var now = DateTime.UtcNow.ToString("O");
        return await connection.ExecuteAsync(
            @"UPDATE user_sessions 
              SET is_revoked = 1, revoked_at = @RevokedAt 
              WHERE id = @Id AND is_revoked = 0;",
            new { Id = sessionId, RevokedAt = now });
    }

    public async Task<int> RevokeAllForUserAsync(string userId)
    {
        using var connection = _connectionFactory.CreateConnection();
        var now = DateTime.UtcNow.ToString("O");
        return await connection.ExecuteAsync(
            @"UPDATE user_sessions 
              SET is_revoked = 1, revoked_at = @RevokedAt 
              WHERE user_id = @UserId AND is_revoked = 0;",
            new { UserId = userId, RevokedAt = now });
    }

    public async Task<IEnumerable<UserSession>> GetActiveSessionsForUserAsync(string userId)
    {
        using var connection = _connectionFactory.CreateConnection();
        var now = DateTime.UtcNow.ToString("O");
        return await connection.QueryAsync<UserSession>(
            @"SELECT id, user_id as UserId, refresh_token_hash as RefreshTokenHash, device_info as DeviceInfo,
                     ip_address as IpAddress, expires_at as ExpiresAt, created_at as CreatedAt,
                     revoked_at as RevokedAt, is_revoked as IsRevoked
              FROM user_sessions 
              WHERE user_id = @UserId AND is_revoked = 0 AND expires_at > @Now
              ORDER BY created_at DESC;",
            new { UserId = userId, Now = now });
    }
}
