using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using WebOffline.Core.Common;
using WebOffline.Core.DTOs;
using WebOffline.Core.Entities;
using WebOffline.Core.Interfaces;
using WebOffline.Infrastructure.Data;
using WebOffline.Infrastructure.Repositories;
using WebOffline.Infrastructure.Services;
using Xunit;

namespace WebOffline.Tests;

public class ServiceLayerTests : IDisposable
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
    private readonly AuditRepository _auditRepo;

    private readonly AuditService _auditService;
    private readonly AbacPolicyEvaluator _abacEvaluator;
    private readonly WorkspaceService _workspaceService;
    private readonly ListService _listService;
    private readonly TaskService _taskService;

    public ServiceLayerTests()
    {
        _mainDbPath = Path.Combine(Path.GetTempPath(), $"main_svc_test_{Guid.NewGuid():N}.db");
        _auditDbPath = Path.Combine(Path.GetTempPath(), $"audit_svc_test_{Guid.NewGuid():N}.db");

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
        _auditRepo = new AuditRepository(_auditConnFactory);

        var config = new ConfigurationBuilder().Build();
        _auditService = new AuditService(_auditRepo, config);
        _abacEvaluator = new AbacPolicyEvaluator(_workspaceRepo, _listRepo, _taskRepo);

        _workspaceService = new WorkspaceService(_workspaceRepo, _userRepo, _abacEvaluator, _auditService);
        _listService = new ListService(_listRepo, _abacEvaluator, _auditService);
        _taskService = new TaskService(_taskRepo, _listRepo, _abacEvaluator, _auditService);
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
    public async Task WorkspaceService_CreateAndRetrieve_Success()
    {
        // Arrange
        var userId = "user-123";
        var userEmail = "dev@example.com";
        await _userRepo.CreateAsync(new User
        {
            Id = userId,
            Email = userEmail,
            PasswordHash = "hash",
            FullName = "Dev User",
            SystemRole = "user",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        // Act
        var createResult = await _workspaceService.CreateWorkspaceAsync(new CreateWorkspaceRequest
        {
            Name = "Engineering Projects",
            Description = "All team projects"
        }, userId, userEmail);

        // Assert
        Assert.True(createResult.Success);
        Assert.NotNull(createResult.Data);
        Assert.Equal("Engineering Projects", createResult.Data.Name);
        Assert.Equal("Owner", createResult.Data.RoleInWorkspace);

        var listResult = await _workspaceService.GetUserWorkspacesAsync(userId);
        Assert.True(listResult.Success);
        Assert.Single(listResult.Data!);
    }

    [Fact]
    public async Task TaskService_ParentTaskCompletionRule_EnforcedViaService()
    {
        // Arrange: User, Workspace, List
        var userId = "user-qa";
        var userEmail = "qa@example.com";
        await _userRepo.CreateAsync(new User
        {
            Id = userId,
            Email = userEmail,
            PasswordHash = "hash",
            FullName = "QA Tester",
            SystemRole = "user",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var wsResult = await _workspaceService.CreateWorkspaceAsync(new CreateWorkspaceRequest
        {
            Name = "Sprint 1",
            Description = "Active Sprint"
        }, userId, userEmail);
        var wsId = wsResult.Data!.Id;

        var listResult = await _listService.CreateListAsync(new CreateListRequest
        {
            WorkspaceId = wsId,
            Name = "Backlog"
        }, userId, userEmail, isSystemAdmin: false);
        var listId = listResult.Data!.Id;

        // Act 1: Create parent task
        var parentResult = await _taskService.CreateTaskAsync(new CreateTaskRequest
        {
            WorkspaceId = wsId,
            ListId = listId,
            Title = "Release v1.0"
        }, userId, userEmail, isSystemAdmin: false);
        Assert.True(parentResult.Success);
        var parentTaskId = parentResult.Data!.Id;

        // Act 2: Create subtask
        var subtaskResult = await _taskService.CreateTaskAsync(new CreateTaskRequest
        {
            WorkspaceId = wsId,
            ListId = listId,
            ParentTaskId = parentTaskId,
            Title = "Write Documentation"
        }, userId, userEmail, isSystemAdmin: false);
        Assert.True(subtaskResult.Success);
        var subtaskId = subtaskResult.Data!.Id;

        // Act 3: Try to complete parent task while subtask is pending
        var completeParentFail = await _taskService.UpdateTaskStatusAsync(parentTaskId, new UpdateTaskStatusRequest
        {
            Status = "DONE"
        }, userId, userEmail, isSystemAdmin: false);

        // Assert 3: Should fail
        Assert.False(completeParentFail.Success);
        Assert.Equal(400, completeParentFail.StatusCode);
        Assert.Contains("incomplete subtask", completeParentFail.Message);

        // Act 4: Complete subtask first
        var completeSubtask = await _taskService.UpdateTaskStatusAsync(subtaskId, new UpdateTaskStatusRequest
        {
            Status = "DONE"
        }, userId, userEmail, isSystemAdmin: false);
        Assert.True(completeSubtask.Success);

        // Act 5: Now complete parent task
        var completeParentSuccess = await _taskService.UpdateTaskStatusAsync(parentTaskId, new UpdateTaskStatusRequest
        {
            Status = "DONE"
        }, userId, userEmail, isSystemAdmin: false);
        Assert.True(completeParentSuccess.Success);
        Assert.Equal("DONE", completeParentSuccess.Data!.Status);

        // Verify audit changelogs in audit DB
        var changelogs = (await _auditRepo.GetChangeLogsAsync(1, 10, "Task", parentTaskId)).ToList();
        Assert.NotEmpty(changelogs);
        Assert.Contains(changelogs, c => c.Action == "STATUS_CHANGE");
    }

    [Fact]
    public async Task CreateTaskAsync_WithoutList_Succeeds()
    {
        var userId = $"user-{Guid.NewGuid():N}";
        var userEmail = "unassigned@example.com";
        await _userRepo.CreateAsync(new User
        {
            Id = userId,
            Email = userEmail,
            PasswordHash = "hash",
            FullName = "Unassigned User",
            SystemRole = "user",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var createWs = await _workspaceService.CreateWorkspaceAsync(new CreateWorkspaceRequest
        {
            Name = "Workspace Without Lists",
            Description = "Testing unassigned tasks"
        }, userId, userEmail);
        Assert.True(createWs.Success);
        var wsId = createWs.Data!.Id;

        // Create task without specifying ListId
        var createTask = await _taskService.CreateTaskAsync(new CreateTaskRequest
        {
            WorkspaceId = wsId,
            ListId = null,
            Title = "Task in Inbox without List",
            Priority = "URGENT"
        }, userId, userEmail, isSystemAdmin: false);

        Assert.True(createTask.Success);
        Assert.NotNull(createTask.Data);
        Assert.Null(createTask.Data.ListId);
        Assert.Equal("Task in Inbox without List", createTask.Data.Title);
        Assert.Equal(wsId, createTask.Data.WorkspaceId);

        // Verify task can be retrieved by workspace
        var getTasks = await _taskService.GetTasksByWorkspaceAsync(wsId, userId, isSystemAdmin: false);
        Assert.True(getTasks.Success);
        Assert.Single(getTasks.Data!);
        Assert.Null(getTasks.Data!.First().ListId);
    }
}

