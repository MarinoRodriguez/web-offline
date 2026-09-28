using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using WebOffline.Core.Common;
using WebOffline.Core.DTOs;
using WebOffline.Core.Entities;
using WebOffline.Infrastructure.Data;
using WebOffline.Infrastructure.Repositories;
using WebOffline.Infrastructure.Services;
using Xunit;

namespace WebOffline.Tests;

public class SyncBatchTests : IDisposable
{
    private readonly string _mainDbPath;
    private readonly string _auditDbPath;
    private readonly ISqliteDbConnectionFactory _mainConnFactory;
    private readonly IAuditDbConnectionFactory _auditConnFactory;
    private readonly DbInitializer _mainDbInit;
    private readonly AuditDbInitializer _auditDbInit;

    private readonly UserRepository _userRepo;
    private readonly WorkspaceRepository _workspaceRepo;
    private readonly ListRepository _listRepo;
    private readonly TaskRepository _taskRepo;
    private readonly SyncRepository _syncRepo;
    private readonly AuditRepository _auditRepo;

    private readonly AuditService _auditService;
    private readonly AbacPolicyEvaluator _abacEvaluator;
    private readonly SyncService _syncService;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SyncBatchTests()
    {
        _mainDbPath = Path.Combine(Path.GetTempPath(), $"main_sync_test_{Guid.NewGuid():N}.db");
        _auditDbPath = Path.Combine(Path.GetTempPath(), $"audit_sync_test_{Guid.NewGuid():N}.db");

        _mainConnFactory = new SqliteDbConnectionFactory($"Data Source={_mainDbPath};Cache=Shared");
        _auditConnFactory = new AuditDbConnectionFactory($"Data Source={_auditDbPath};Cache=Shared");

        _mainDbInit = new DbInitializer(_mainConnFactory, new BcryptPasswordHasher());
        _auditDbInit = new AuditDbInitializer(_auditConnFactory);

        _mainDbInit.InitializeAsync().GetAwaiter().GetResult();
        _auditDbInit.InitializeAsync().GetAwaiter().GetResult();

        _userRepo = new UserRepository(_mainConnFactory);
        _workspaceRepo = new WorkspaceRepository(_mainConnFactory);
        _listRepo = new ListRepository(_mainConnFactory);
        _taskRepo = new TaskRepository(_mainConnFactory);
        _syncRepo = new SyncRepository(_mainConnFactory);
        _auditRepo = new AuditRepository(_auditConnFactory);

        var config = new ConfigurationBuilder().Build();
        _auditService = new AuditService(_auditRepo, config);
        _abacEvaluator = new AbacPolicyEvaluator(_workspaceRepo, _listRepo, _taskRepo);

        _syncService = new SyncService(
            _syncRepo,
            _workspaceRepo,
            _listRepo,
            _taskRepo,
            _abacEvaluator,
            _auditService);
    }

    public void Dispose()
    {
        if (File.Exists(_mainDbPath))
        {
            try { File.Delete(_mainDbPath); } catch { }
        }
        if (File.Exists(_auditDbPath))
        {
            try { File.Delete(_auditDbPath); } catch { }
        }
    }

    [Fact]
    public async Task SyncBatch_PushNewWorkspaceListAndTask_AppliesSuccessfully()
    {
        // Arrange
        var userId = "user-sync-1";
        var userEmail = "sync1@example.com";
        await _userRepo.CreateAsync(new User
        {
            Id = userId,
            Email = userEmail,
            PasswordHash = "hash",
            FullName = "Sync User 1",
            SystemRole = "user",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var wsId = Guid.NewGuid().ToString();
        var listId = Guid.NewGuid().ToString();
        var taskId = Guid.NewGuid().ToString();
        var clientNow = DateTime.UtcNow;

        var request = new SyncBatchRequest
        {
            LastSyncedAt = null,
            Mutations = new List<SyncMutationDto>
            {
                new()
                {
                    Id = "m1",
                    Entity = "workspace",
                    Operation = "INSERT",
                    EntityId = wsId,
                    ClientTimestamp = clientNow,
                    Version = 1,
                    Payload = JsonSerializer.SerializeToElement(new { Name = "Offline Workspace", Description = "Created offline" }, _jsonOptions)
                },
                new()
                {
                    Id = "m2",
                    Entity = "list",
                    Operation = "INSERT",
                    EntityId = listId,
                    WorkspaceId = wsId,
                    ClientTimestamp = clientNow,
                    Version = 1,
                    Payload = JsonSerializer.SerializeToElement(new { WorkspaceId = wsId, Name = "Sprint Tasks", Color = "#10B981" }, _jsonOptions)
                },
                new()
                {
                    Id = "m3",
                    Entity = "task",
                    Operation = "INSERT",
                    EntityId = taskId,
                    WorkspaceId = wsId,
                    ClientTimestamp = clientNow,
                    Version = 1,
                    Payload = JsonSerializer.SerializeToElement(new { ListId = listId, WorkspaceId = wsId, Title = "Setup PWA", Priority = "HIGH" }, _jsonOptions)
                }
            }
        };

        // Act
        var result = await _syncService.SyncBatchAsync(request, userId, userEmail, isSystemAdmin: false);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(3, result.Data.AppliedCount);
        Assert.Empty(result.Data.Rejections);

        // Verify entities were created in server DB
        var savedWs = await _workspaceRepo.GetByIdAsync(wsId);
        Assert.NotNull(savedWs);
        Assert.Equal("Offline Workspace", savedWs.Name);

        var savedList = await _listRepo.GetByIdAsync(listId);
        Assert.NotNull(savedList);
        Assert.Equal("Sprint Tasks", savedList.Name);

        var savedTask = await _taskRepo.GetByIdAsync(taskId);
        Assert.NotNull(savedTask);
        Assert.Equal("Setup PWA", savedTask.Title);

        // Verify Pull inside batch returns the newly created entities
        Assert.Single(result.Data.Changes.Workspaces);
        Assert.Single(result.Data.Changes.Lists);
        Assert.Single(result.Data.Changes.Tasks);
    }

    [Fact]
    public async Task SyncBatch_SubtaskCompletionRule_RejectsPrematureParentDone()
    {
        // Arrange
        var userId = "user-sync-2";
        var userEmail = "sync2@example.com";
        await _userRepo.CreateAsync(new User
        {
            Id = userId,
            Email = userEmail,
            PasswordHash = "hash",
            FullName = "Sync User 2",
            SystemRole = "user",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var wsId = Guid.NewGuid().ToString();
        var listId = Guid.NewGuid().ToString();
        var parentTaskId = Guid.NewGuid().ToString();
        var subtaskId = Guid.NewGuid().ToString();

        // 1. Initial insert of workspace, list, parent task, subtask
        await _syncService.SyncBatchAsync(new SyncBatchRequest
        {
            Mutations = new List<SyncMutationDto>
            {
                new()
                {
                    Id = "m1",
                    Entity = "workspace",
                    Operation = "INSERT",
                    EntityId = wsId,
                    ClientTimestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.SerializeToElement(new { Name = "Project Alpha" })
                },
                new()
                {
                    Id = "m2",
                    Entity = "list",
                    Operation = "INSERT",
                    EntityId = listId,
                    WorkspaceId = wsId,
                    ClientTimestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.SerializeToElement(new { WorkspaceId = wsId, Name = "Tasks" })
                },
                new()
                {
                    Id = "m3",
                    Entity = "task",
                    Operation = "INSERT",
                    EntityId = parentTaskId,
                    WorkspaceId = wsId,
                    ClientTimestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.SerializeToElement(new { ListId = listId, Title = "Parent Feature", Status = "TODO" })
                },
                new()
                {
                    Id = "m4",
                    Entity = "task",
                    Operation = "INSERT",
                    EntityId = subtaskId,
                    WorkspaceId = wsId,
                    ClientTimestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.SerializeToElement(new { ListId = listId, ParentTaskId = parentTaskId, Title = "Child Step 1", Status = "TODO" })
                }
            }
        }, userId, userEmail, false);

        // Act 1: Attempt to set parent task to DONE while subtask is TODO
        var failResult = await _syncService.SyncBatchAsync(new SyncBatchRequest
        {
            Mutations = new List<SyncMutationDto>
            {
                new()
                {
                    Id = "m-parent-done-fail",
                    Entity = "task",
                    Operation = "UPDATE",
                    EntityId = parentTaskId,
                    WorkspaceId = wsId,
                    ClientTimestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.SerializeToElement(new { Status = "DONE" })
                }
            }
        }, userId, userEmail, false);

        // Assert 1: Rejected
        Assert.Single(failResult.Data!.Rejections);
        Assert.Equal("m-parent-done-fail", failResult.Data.Rejections[0].MutationId);
        Assert.Contains("incomplete subtasks", failResult.Data.Rejections[0].Reason);
        Assert.Equal(400, failResult.Data.Rejections[0].StatusCode);

        // Act 2: Complete subtask first, then complete parent
        var successResult = await _syncService.SyncBatchAsync(new SyncBatchRequest
        {
            Mutations = new List<SyncMutationDto>
            {
                new()
                {
                    Id = "m-sub-done",
                    Entity = "task",
                    Operation = "UPDATE",
                    EntityId = subtaskId,
                    WorkspaceId = wsId,
                    ClientTimestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.SerializeToElement(new { Status = "DONE" })
                },
                new()
                {
                    Id = "m-parent-done-success",
                    Entity = "task",
                    Operation = "UPDATE",
                    EntityId = parentTaskId,
                    WorkspaceId = wsId,
                    ClientTimestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.SerializeToElement(new { Status = "DONE" })
                }
            }
        }, userId, userEmail, false);

        // Assert 2: Both succeeded
        Assert.Empty(successResult.Data!.Rejections);
        Assert.Equal(2, successResult.Data.AppliedCount);

        var parentInDb = await _taskRepo.GetByIdAsync(parentTaskId);
        Assert.Equal("DONE", parentInDb!.Status);
    }

    [Fact]
    public async Task SyncBatch_ConflictResolution_RejectsStaleClientTimestamp()
    {
        // Arrange
        var userId = "user-sync-3";
        var userEmail = "sync3@example.com";
        await _userRepo.CreateAsync(new User
        {
            Id = userId,
            Email = userEmail,
            PasswordHash = "hash",
            FullName = "Sync User 3",
            SystemRole = "user",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var wsId = Guid.NewGuid().ToString();
        var t0 = DateTime.UtcNow;

        await _syncService.SyncBatchAsync(new SyncBatchRequest
        {
            Mutations = new List<SyncMutationDto>
            {
                new()
                {
                    Id = "m-ws",
                    Entity = "workspace",
                    Operation = "INSERT",
                    EntityId = wsId,
                    ClientTimestamp = t0,
                    Payload = JsonSerializer.SerializeToElement(new { Name = "Original Name" })
                }
            }
        }, userId, userEmail, false);

        // Simulate server updated more recently
        var wsInDb = await _workspaceRepo.GetByIdAsync(wsId);
        wsInDb!.Name = "Server Updated Name";
        wsInDb.UpdatedAt = DateTime.UtcNow.AddMinutes(5);
        await _workspaceRepo.UpdateAsync(wsInDb);

        // Act: Client sends mutation with an older timestamp (T0)
        var staleResult = await _syncService.SyncBatchAsync(new SyncBatchRequest
        {
            Mutations = new List<SyncMutationDto>
            {
                new()
                {
                    Id = "m-stale-update",
                    Entity = "workspace",
                    Operation = "UPDATE",
                    EntityId = wsId,
                    ClientTimestamp = t0, // Stale!
                    Payload = JsonSerializer.SerializeToElement(new { Name = "Stale Client Name" })
                }
            }
        }, userId, userEmail, false);

        // Assert: Rejected with 409 Conflict
        Assert.Single(staleResult.Data!.Rejections);
        Assert.Equal(409, staleResult.Data.Rejections[0].StatusCode);
        Assert.Contains("Conflict", staleResult.Data.Rejections[0].Reason);

        var finalWs = await _workspaceRepo.GetByIdAsync(wsId);
        Assert.Equal("Server Updated Name", finalWs!.Name);
    }

    [Fact]
    public async Task SyncBatch_DeleteOperation_RecordsTombstoneAndReturnsInPull()
    {
        // Arrange
        var userId = "user-sync-4";
        var userEmail = "sync4@example.com";
        await _userRepo.CreateAsync(new User
        {
            Id = userId,
            Email = userEmail,
            PasswordHash = "hash",
            FullName = "Sync User 4",
            SystemRole = "user",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var wsId = Guid.NewGuid().ToString();
        var listId = Guid.NewGuid().ToString();
        var taskId = Guid.NewGuid().ToString();
        var t0 = DateTime.UtcNow.AddMinutes(-10);

        await _syncService.SyncBatchAsync(new SyncBatchRequest
        {
            Mutations = new List<SyncMutationDto>
            {
                new()
                {
                    Id = "m-ws",
                    Entity = "workspace",
                    Operation = "INSERT",
                    EntityId = wsId,
                    ClientTimestamp = t0,
                    Payload = JsonSerializer.SerializeToElement(new { Name = "WS Deletion Test" })
                },
                new()
                {
                    Id = "m-list",
                    Entity = "list",
                    Operation = "INSERT",
                    EntityId = listId,
                    WorkspaceId = wsId,
                    ClientTimestamp = t0,
                    Payload = JsonSerializer.SerializeToElement(new { WorkspaceId = wsId, Name = "List to delete" })
                },
                new()
                {
                    Id = "m-task",
                    Entity = "task",
                    Operation = "INSERT",
                    EntityId = taskId,
                    WorkspaceId = wsId,
                    ClientTimestamp = t0,
                    Payload = JsonSerializer.SerializeToElement(new { ListId = listId, WorkspaceId = wsId, Title = "Task to delete" })
                }
            }
        }, userId, userEmail, false);

        var lastSyncMarker = DateTime.UtcNow;

        // Act: Delete the task via sync
        var deleteResult = await _syncService.SyncBatchAsync(new SyncBatchRequest
        {
            LastSyncedAt = lastSyncMarker,
            Mutations = new List<SyncMutationDto>
            {
                new()
                {
                    Id = "m-del-task",
                    Entity = "task",
                    Operation = "DELETE",
                    EntityId = taskId,
                    WorkspaceId = wsId,
                    ClientTimestamp = DateTime.UtcNow
                }
            }
        }, userId, userEmail, false);

        // Assert: Task was deleted and tombstone generated
        Assert.True(deleteResult.Success);
        Assert.Equal(1, deleteResult.Data!.AppliedCount);
        Assert.Empty(deleteResult.Data.Rejections);

        // Client pull with lastSyncMarker should return the tombstone!
        var pullResult = await _syncService.PullChangesAsync(lastSyncMarker, wsId, userId, false);
        Assert.True(pullResult.Success);
        Assert.Contains(pullResult.Data!.Changes.Tombstones, t => t.EntityId == taskId && t.EntityType == "task");
    }

    [Fact]
    public async Task SyncBatch_AbacUnauthorized_RejectsMutation()
    {
        // Arrange: User 1 owns workspace
        var ownerId = "owner-1";
        var ownerEmail = "owner@example.com";
        await _userRepo.CreateAsync(new User
        {
            Id = ownerId,
            Email = ownerEmail,
            PasswordHash = "hash",
            FullName = "Owner User",
            SystemRole = "user",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        // User 2 (stranger with no membership)
        var strangerId = "stranger-2";
        var strangerEmail = "stranger@example.com";
        await _userRepo.CreateAsync(new User
        {
            Id = strangerId,
            Email = strangerEmail,
            PasswordHash = "hash",
            FullName = "Stranger User",
            SystemRole = "user",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var wsId = Guid.NewGuid().ToString();
        var listId = Guid.NewGuid().ToString();

        // Owner creates workspace
        await _syncService.SyncBatchAsync(new SyncBatchRequest
        {
            Mutations = new List<SyncMutationDto>
            {
                new()
                {
                    Id = "m-ws-owner",
                    Entity = "workspace",
                    Operation = "INSERT",
                    EntityId = wsId,
                    ClientTimestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.SerializeToElement(new { Name = "Owner Private WS" })
                }
            }
        }, ownerId, ownerEmail, false);

        // Act: Stranger attempts to create a list in owner's workspace
        var result = await _syncService.SyncBatchAsync(new SyncBatchRequest
        {
            Mutations = new List<SyncMutationDto>
            {
                new()
                {
                    Id = "m-intruder-list",
                    Entity = "list",
                    Operation = "INSERT",
                    EntityId = listId,
                    WorkspaceId = wsId,
                    ClientTimestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.SerializeToElement(new { WorkspaceId = wsId, Name = "Unauthorized List" })
                }
            }
        }, strangerId, strangerEmail, false);

        // Assert: 403 Forbidden rejection
        Assert.Single(result.Data!.Rejections);
        Assert.Equal(403, result.Data.Rejections[0].StatusCode);
        Assert.Contains("Access denied", result.Data.Rejections[0].Reason);
    }

    [Fact]
    public async Task SyncBatch_AuditIntegration_VerifiesTamperProofChangelog()
    {
        // Arrange
        var userId = "user-audit-sync";
        var userEmail = "auditsync@example.com";
        await _userRepo.CreateAsync(new User
        {
            Id = userId,
            Email = userEmail,
            PasswordHash = "hash",
            FullName = "Audit Sync User",
            SystemRole = "user",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var wsId = Guid.NewGuid().ToString();

        // Act: Create workspace via sync
        var syncResult = await _syncService.SyncBatchAsync(new SyncBatchRequest
        {
            Mutations = new List<SyncMutationDto>
            {
                new()
                {
                    Id = "m-audit-ws",
                    Entity = "workspace",
                    Operation = "INSERT",
                    EntityId = wsId,
                    ClientTimestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.SerializeToElement(new { Name = "Audit Verified WS" })
                }
            }
        }, userId, userEmail, false);

        // Assert
        Assert.True(syncResult.Success);

        // Verify changelog written to audit DB
        var changelogs = (await _auditRepo.GetChangeLogsAsync(1, 10, "Workspace", wsId)).ToList();
        Assert.NotEmpty(changelogs);
        var log = changelogs.First();
        Assert.Equal("SYNC_CREATE", log.Action);
        Assert.Equal(userId, log.UserId);

        // Verify cryptographic hash integrity!
        var isTamperProof = _auditService.VerifyChangeLogIntegrity(log);
        Assert.True(isTamperProof);
    }
}
