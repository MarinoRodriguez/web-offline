using System;
using System.IO;
using System.Threading.Tasks;
using WebOffline.Api.Security.Abac;
using WebOffline.Core.Entities;
using WebOffline.Infrastructure.Data;
using WebOffline.Infrastructure.Repositories;
using WebOffline.Infrastructure.Services;
using Xunit;

namespace WebOffline.Tests;

public class AbacAndSubtasksTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ISqliteDbConnectionFactory _connectionFactory;
    private readonly DbInitializer _dbInitializer;
    private readonly WorkspaceRepository _workspaceRepository;
    private readonly ListRepository _listRepository;
    private readonly TaskRepository _taskRepository;
    private readonly AbacPolicyEvaluator _abacEvaluator;

    private readonly UserRepository _userRepository;

    public AbacAndSubtasksTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_abac_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath};";
        _connectionFactory = new SqliteDbConnectionFactory(connectionString);
        _dbInitializer = new DbInitializer(_connectionFactory, new BcryptPasswordHasher());
        _userRepository = new UserRepository(_connectionFactory);
        _workspaceRepository = new WorkspaceRepository(_connectionFactory);
        _listRepository = new ListRepository(_connectionFactory);
        _taskRepository = new TaskRepository(_connectionFactory);
        _abacEvaluator = new AbacPolicyEvaluator(_workspaceRepository, _listRepository, _taskRepository);

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
    public async Task Abac_WorkspacePermissions_ShouldBeProperlyEnforced()
    {
        var ownerId = Guid.NewGuid().ToString();
        var editorId = Guid.NewGuid().ToString();
        var viewerId = Guid.NewGuid().ToString();
        var strangerId = Guid.NewGuid().ToString();

        await _userRepository.CreateAsync(new User { Id = ownerId, Email = "owner@test.com", FullName = "Owner", PasswordHash = "x", SystemRole = "user" });
        await _userRepository.CreateAsync(new User { Id = editorId, Email = "editor@test.com", FullName = "Editor", PasswordHash = "x", SystemRole = "user" });
        await _userRepository.CreateAsync(new User { Id = viewerId, Email = "viewer@test.com", FullName = "Viewer", PasswordHash = "x", SystemRole = "user" });
        await _userRepository.CreateAsync(new User { Id = strangerId, Email = "stranger@test.com", FullName = "Stranger", PasswordHash = "x", SystemRole = "user" });

        var ws = new Workspace
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Marketing Workspace",
            OwnerId = ownerId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _workspaceRepository.CreateAsync(ws);

        await _workspaceRepository.AddMemberAsync(new WorkspaceMember
        {
            WorkspaceId = ws.Id,
            UserId = editorId,
            Role = "Editor",
            JoinedAt = DateTime.UtcNow
        });

        await _workspaceRepository.AddMemberAsync(new WorkspaceMember
        {
            WorkspaceId = ws.Id,
            UserId = viewerId,
            Role = "Viewer",
            JoinedAt = DateTime.UtcNow
        });

        // 1. Owner permissions
        Assert.True(await _abacEvaluator.CanAccessWorkspaceAsync(ownerId, ws.Id, ResourceAction.Read));
        Assert.True(await _abacEvaluator.CanAccessWorkspaceAsync(ownerId, ws.Id, ResourceAction.Write));
        Assert.True(await _abacEvaluator.CanAccessWorkspaceAsync(ownerId, ws.Id, ResourceAction.Delete));
        Assert.True(await _abacEvaluator.CanAccessWorkspaceAsync(ownerId, ws.Id, ResourceAction.Admin));

        // 2. Editor permissions
        Assert.True(await _abacEvaluator.CanAccessWorkspaceAsync(editorId, ws.Id, ResourceAction.Read));
        Assert.True(await _abacEvaluator.CanAccessWorkspaceAsync(editorId, ws.Id, ResourceAction.Write));
        Assert.False(await _abacEvaluator.CanAccessWorkspaceAsync(editorId, ws.Id, ResourceAction.Delete));
        Assert.False(await _abacEvaluator.CanAccessWorkspaceAsync(editorId, ws.Id, ResourceAction.Admin));

        // 3. Viewer permissions
        Assert.True(await _abacEvaluator.CanAccessWorkspaceAsync(viewerId, ws.Id, ResourceAction.Read));
        Assert.False(await _abacEvaluator.CanAccessWorkspaceAsync(viewerId, ws.Id, ResourceAction.Write));
        Assert.False(await _abacEvaluator.CanAccessWorkspaceAsync(viewerId, ws.Id, ResourceAction.Delete));

        // 4. Stranger permissions
        Assert.False(await _abacEvaluator.CanAccessWorkspaceAsync(strangerId, ws.Id, ResourceAction.Read));

        // 5. System Admin override
        Assert.True(await _abacEvaluator.CanAccessWorkspaceAsync(strangerId, ws.Id, ResourceAction.Delete, isSystemAdmin: true));
    }

    [Fact]
    public async Task SubtasksRule_ParentCannotBeCompletedUntilAllSubtasksAreDone()
    {
        var userId = Guid.NewGuid().ToString();
        await _userRepository.CreateAsync(new User { Id = userId, Email = "u1@test.com", FullName = "U1", PasswordHash = "x", SystemRole = "user" });

        var wsId = Guid.NewGuid().ToString();
        var listId = Guid.NewGuid().ToString();
        var parentTaskId = Guid.NewGuid().ToString();
        var subtask1Id = Guid.NewGuid().ToString();
        var subtask2Id = Guid.NewGuid().ToString();

        var ws = new Workspace { Id = wsId, Name = "Test WS", OwnerId = userId };
        await _workspaceRepository.CreateAsync(ws);

        var list = new TaskList { Id = listId, WorkspaceId = wsId, Name = "Sprint 1" };
        await _listRepository.CreateAsync(list);

        var parentTask = new TaskItem
        {
            Id = parentTaskId,
            ListId = listId,
            WorkspaceId = wsId,
            Title = "Release v1.0",
            Status = "TODO",
            CreatedBy = userId
        };
        await _taskRepository.CreateAsync(parentTask);

        var subtask1 = new TaskItem
        {
            Id = subtask1Id,
            ListId = listId,
            WorkspaceId = wsId,
            ParentTaskId = parentTaskId,
            Title = "Mini Task: Write Unit Tests",
            Status = "DONE",
            CreatedBy = userId
        };
        var subtask2 = new TaskItem
        {
            Id = subtask2Id,
            ListId = listId,
            WorkspaceId = wsId,
            ParentTaskId = parentTaskId,
            Title = "Mini Task: Deploy to Staging",
            Status = "TODO", // Pending!
            CreatedBy = userId
        };

        await _taskRepository.CreateAsync(subtask1);
        await _taskRepository.CreateAsync(subtask2);

        // Verification: Not all subtasks are completed
        var allDoneBefore = await _taskRepository.AreAllSubtasksCompletedAsync(parentTaskId);
        var pendingCount = await _taskRepository.CountPendingSubtasksAsync(parentTaskId);

        Assert.False(allDoneBefore);
        Assert.Equal(1, pendingCount);

        // Complete the remaining subtask
        await _taskRepository.UpdateStatusAsync(subtask2Id, "DONE", 2);

        // Verification: Now all subtasks are completed
        var allDoneAfter = await _taskRepository.AreAllSubtasksCompletedAsync(parentTaskId);
        var pendingCountAfter = await _taskRepository.CountPendingSubtasksAsync(parentTaskId);

        Assert.True(allDoneAfter);
        Assert.Equal(0, pendingCountAfter);
    }
}
