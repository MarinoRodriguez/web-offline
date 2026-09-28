using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using WebOffline.Core.Entities;
using WebOffline.Infrastructure.Data;
using WebOffline.Infrastructure.Repositories;
using WebOffline.Infrastructure.Services;
using Xunit;

namespace WebOffline.Tests;

public class AuthAndSessionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ISqliteDbConnectionFactory _connectionFactory;
    private readonly IPasswordHasher _passwordHasher;
    private readonly DbInitializer _dbInitializer;
    private readonly UserRepository _userRepository;
    private readonly UserSessionRepository _sessionRepository;
    private readonly JwtTokenService _tokenService;

    public AuthAndSessionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_auth_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath};";
        _connectionFactory = new SqliteDbConnectionFactory(connectionString);
        _passwordHasher = new BcryptPasswordHasher();
        _dbInitializer = new DbInitializer(_connectionFactory, _passwordHasher);
        _userRepository = new UserRepository(_connectionFactory);
        _sessionRepository = new UserSessionRepository(_connectionFactory);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>("Jwt:Secret", "A_Very_Long_Super_Secret_Key_For_Testing_123456789!"),
                new System.Collections.Generic.KeyValuePair<string, string?>("Jwt:Issuer", "TestIssuer"),
                new System.Collections.Generic.KeyValuePair<string, string?>("Jwt:Audience", "TestAudience"),
                new System.Collections.Generic.KeyValuePair<string, string?>("Jwt:AccessTokenMinutes", "15")
            })
            .Build();

        _tokenService = new JwtTokenService(config);

        _dbInitializer.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch { }
    }

    [Fact]
    public async Task DbInitializer_ShouldSeedDefaultAdminUser()
    {
        var admin = await _userRepository.GetByEmailAsync("admin@offline.local");
        Assert.NotNull(admin);
        Assert.Equal("admin", admin.SystemRole);
        Assert.True(_passwordHasher.VerifyPassword("Admin123!", admin.PasswordHash));
    }

    [Fact]
    public async Task SessionRevocation_ShouldProperlyInvalidateSession()
    {
        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Email = "john@example.com",
            FullName = "John Doe",
            PasswordHash = _passwordHasher.HashPassword("Secret123!"),
            SystemRole = "user"
        };
        await _userRepository.CreateAsync(user);

        var sessionId = Guid.NewGuid().ToString();
        var refreshToken = _tokenService.GenerateRefreshToken();
        var session = new UserSession
        {
            Id = sessionId,
            UserId = user.Id,
            RefreshTokenHash = _tokenService.HashToken(refreshToken),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow,
            IsRevoked = false
        };

        await _sessionRepository.CreateAsync(session);

        var saved = await _sessionRepository.GetByIdAsync(sessionId);
        Assert.NotNull(saved);
        Assert.False(saved.IsRevoked);
        Assert.True(saved.IsActiveSession);

        // Revoke the session
        await _sessionRepository.RevokeAsync(sessionId);

        var revoked = await _sessionRepository.GetByIdAsync(sessionId);
        Assert.NotNull(revoked);
        Assert.True(revoked.IsRevoked);
        Assert.False(revoked.IsActiveSession);
    }

    [Fact]
    public async Task RevokeAllForUser_ShouldInvalidateAllUserSessions()
    {
        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Email = $"multi_{Guid.NewGuid():N}@example.com",
            FullName = "Multi Session User",
            PasswordHash = "hash",
            SystemRole = "user"
        };
        await _userRepository.CreateAsync(user);

        for (int i = 0; i < 3; i++)
        {
            var session = new UserSession
            {
                Id = Guid.NewGuid().ToString(),
                UserId = user.Id,
                RefreshTokenHash = _tokenService.HashToken($"token_{i}"),
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow,
                IsRevoked = false
            };
            await _sessionRepository.CreateAsync(session);
        }

        var activeBefore = await _sessionRepository.GetActiveSessionsForUserAsync(user.Id);
        Assert.Equal(3, System.Linq.Enumerable.Count(activeBefore));

        // Revoke all
        await _sessionRepository.RevokeAllForUserAsync(user.Id);

        var activeAfter = await _sessionRepository.GetActiveSessionsForUserAsync(user.Id);
        Assert.Empty(activeAfter);
    }
}
