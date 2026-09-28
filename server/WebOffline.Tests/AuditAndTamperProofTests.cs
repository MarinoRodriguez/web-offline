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

public class AuditAndTamperProofTests : IDisposable
{
    private readonly string _auditDbPath;
    private readonly IAuditDbConnectionFactory _auditDbConnectionFactory;
    private readonly AuditDbInitializer _auditDbInitializer;
    private readonly AuditRepository _auditRepository;
    private readonly AuditService _auditService;

    public AuditAndTamperProofTests()
    {
        _auditDbPath = Path.Combine(Path.GetTempPath(), $"test_audit_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_auditDbPath};";
        _auditDbConnectionFactory = new AuditDbConnectionFactory(connectionString);
        _auditDbInitializer = new AuditDbInitializer(_auditDbConnectionFactory);
        _auditRepository = new AuditRepository(_auditDbConnectionFactory);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>("Audit:SecretKey", "Test_Secret_Audit_Key_12345!")
            })
            .Build();

        _auditService = new AuditService(_auditRepository, config);

        _auditDbInitializer.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_auditDbPath))
            {
                File.Delete(_auditDbPath);
            }
        }
        catch { }
    }

    [Fact]
    public async Task AuditRepository_ShouldStoreAndRetrieveHttpRequestLogs()
    {
        var log = new HttpRequestLog
        {
            Id = Guid.NewGuid().ToString(),
            CorrelationId = Guid.NewGuid().ToString(),
            UserId = "user-123",
            UserEmail = "user@test.com",
            ClientIp = "192.168.1.100",
            UserAgent = "Mozilla/5.0 OfflineTest",
            HttpMethod = "POST",
            Path = "/api/tasks",
            QueryString = "?filter=urgent",
            RequestBody = "{\"title\":\"Test Task\"}",
            StatusCode = 201,
            ResponseBody = "{\"success\":true,\"data\":{\"id\":\"task-1\"}}",
            DurationMs = 45,
            CreatedAt = DateTime.UtcNow
        };

        await _auditRepository.LogRequestAsync(log);

        var items = await _auditRepository.GetRequestLogsAsync(page: 1, pageSize: 10, userId: "user-123");
        var retrieved = Assert.Single(items);

        Assert.Equal(log.Id, retrieved.Id);
        Assert.Equal("user@test.com", retrieved.UserEmail);
        Assert.Equal("POST", retrieved.HttpMethod);
        Assert.Equal("/api/tasks", retrieved.Path);
        Assert.Equal(201, retrieved.StatusCode);
        Assert.Equal(45, retrieved.DurationMs);
    }

    [Fact]
    public async Task AuditService_ShouldGenerateTamperProofChangelogAndDetectModifications()
    {
        var taskOld = new TaskItem
        {
            Id = "task-99",
            Title = "Original Title",
            Status = "TODO",
            Priority = "LOW",
            Version = 1
        };

        var taskNew = new TaskItem
        {
            Id = "task-99",
            Title = "Modified Title",
            Status = "IN_PROGRESS",
            Priority = "HIGH",
            Version = 2
        };

        // 1. Record genuine changelog
        var changelog = await _auditService.RecordChangeAsync(
            userId: "user-admin",
            userEmail: "admin@offline.local",
            action: "UPDATE",
            entityType: "Task",
            entityId: taskNew.Id,
            description: "admin@offline.local modificó la tarea 'Modified Title'",
            oldState: taskOld,
            newState: taskNew);

        Assert.NotNull(changelog);
        Assert.NotEmpty(changelog.TamperHash);
        Assert.Contains("Modified Title", changelog.DiffJson);

        // 2. Verify genuine changelog -> must pass integrity verification
        var isGenuineValid = _auditService.VerifyChangeLogIntegrity(changelog);
        Assert.True(isGenuineValid);

        // 3. Simulate an adversary tampering with the DiffJson in the database
        var tamperedChangelog = new AuditChangeLog
        {
            Id = changelog.Id,
            Timestamp = changelog.Timestamp,
            UserId = changelog.UserId,
            UserEmail = changelog.UserEmail,
            Action = changelog.Action,
            EntityType = changelog.EntityType,
            EntityId = changelog.EntityId,
            Description = changelog.Description,
            DiffJson = changelog.DiffJson?.Replace("Modified Title", "Maliciously Altered Title"),
            PrevHash = changelog.PrevHash,
            TamperHash = changelog.TamperHash // Stored hash doesn't match altered diff!
        };

        var isTamperedValid = _auditService.VerifyChangeLogIntegrity(tamperedChangelog);
        Assert.False(isTamperedValid); // Must detect tampering!
    }

    [Fact]
    public async Task AuditChangelogs_ShouldChainHashesAcrossMultipleActions()
    {
        // First entry (genesis, no prev hash)
        var entry1 = await _auditService.RecordChangeAsync(
            "u1", "u1@test.com", "CREATE", "Task", "t1", "Created t1",
            (TaskItem?)null, new TaskItem { Id = "t1", Title = "T1" });

        Assert.Null(entry1.PrevHash);

        // Second entry must point to entry1's TamperHash
        var entry2 = await _auditService.RecordChangeAsync(
            "u1", "u1@test.com", "UPDATE", "Task", "t1", "Updated t1",
            new TaskItem { Id = "t1", Title = "T1" }, new TaskItem { Id = "t1", Title = "T1 v2" });

        Assert.Equal(entry1.TamperHash, entry2.PrevHash);

        // Both must be valid
        Assert.True(_auditService.VerifyChangeLogIntegrity(entry1));
        Assert.True(_auditService.VerifyChangeLogIntegrity(entry2));
    }
}
