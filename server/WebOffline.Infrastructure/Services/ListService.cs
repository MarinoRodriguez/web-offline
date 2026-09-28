using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WebOffline.Core.Common;
using WebOffline.Core.DTOs;
using WebOffline.Core.Entities;
using WebOffline.Core.Interfaces;

namespace WebOffline.Infrastructure.Services;

public class ListService : IListService
{
    private readonly IListRepository _listRepository;
    private readonly IAbacPolicyEvaluator _abacEvaluator;
    private readonly IAuditService _auditService;

    public ListService(
        IListRepository listRepository,
        IAbacPolicyEvaluator abacEvaluator,
        IAuditService auditService)
    {
        _listRepository = listRepository;
        _abacEvaluator = abacEvaluator;
        _auditService = auditService;
    }

    public async Task<ApiResponse<IEnumerable<ListDto>>> GetListsByWorkspaceAsync(string workspaceId, string currentUserId, bool isSystemAdmin)
    {
        var canRead = await _abacEvaluator.CanAccessWorkspaceAsync(currentUserId, workspaceId, ResourceAction.Read, isSystemAdmin);
        if (!canRead)
        {
            return ApiResponse<IEnumerable<ListDto>>.Fail("Access denied.", 403);
        }

        var lists = await _listRepository.GetByWorkspaceIdAsync(workspaceId);
        var dtos = lists.Select(l => new ListDto
        {
            Id = l.Id,
            WorkspaceId = l.WorkspaceId,
            Name = l.Name,
            Color = l.Color,
            Position = l.Position,
            CreatedBy = l.CreatedBy,
            CreatedAt = l.CreatedAt,
            UpdatedBy = l.UpdatedBy,
            UpdatedAt = l.UpdatedAt,
            Version = l.Version
        });

        return ApiResponse<IEnumerable<ListDto>>.Ok(dtos);
    }

    public async Task<ApiResponse<ListDto>> GetListByIdAsync(string listId, string currentUserId, bool isSystemAdmin)
    {
        var list = await _listRepository.GetByIdAsync(listId);
        if (list == null)
        {
            return ApiResponse<ListDto>.Fail("List not found.", 404);
        }

        var canRead = await _abacEvaluator.CanAccessWorkspaceAsync(currentUserId, list.WorkspaceId, ResourceAction.Read, isSystemAdmin);
        if (!canRead)
        {
            return ApiResponse<ListDto>.Fail("Access denied.", 403);
        }

        var dto = new ListDto
        {
            Id = list.Id,
            WorkspaceId = list.WorkspaceId,
            Name = list.Name,
            Color = list.Color,
            Position = list.Position,
            CreatedBy = list.CreatedBy,
            CreatedAt = list.CreatedAt,
            UpdatedBy = list.UpdatedBy,
            UpdatedAt = list.UpdatedAt,
            Version = list.Version
        };

        return ApiResponse<ListDto>.Ok(dto);
    }

    public async Task<ApiResponse<ListDto>> CreateListAsync(CreateListRequest request, string currentUserId, string currentUserEmail, bool isSystemAdmin)
    {
        if (string.IsNullOrWhiteSpace(request.WorkspaceId) || string.IsNullOrWhiteSpace(request.Name))
        {
            return ApiResponse<ListDto>.Fail("WorkspaceId and Name are required.", 400);
        }

        var canWrite = await _abacEvaluator.CanAccessWorkspaceAsync(currentUserId, request.WorkspaceId, ResourceAction.Write, isSystemAdmin);
        if (!canWrite)
        {
            return ApiResponse<ListDto>.Fail("Access denied: You need editor permissions in this workspace.", 403);
        }

        var list = new TaskList
        {
            Id = Guid.NewGuid().ToString(),
            WorkspaceId = request.WorkspaceId,
            Name = request.Name.Trim(),
            Color = string.IsNullOrWhiteSpace(request.Color) ? "#3B82F6" : request.Color,
            Position = request.Position,
            CreatedBy = currentUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedBy = currentUserId,
            UpdatedAt = DateTime.UtcNow,
            Version = 1,
            IsDeleted = false
        };

        await _listRepository.CreateAsync(list);

        // Audit changelog
        await _auditService.RecordChangeAsync(
            currentUserId,
            currentUserEmail,
            "CREATE",
            "List",
            list.Id,
            $"{currentUserEmail} creó la lista '{list.Name}' en el workspace '{list.WorkspaceId}'",
            (TaskList?)null,
            list);

        var dto = new ListDto
        {
            Id = list.Id,
            WorkspaceId = list.WorkspaceId,
            Name = list.Name,
            Color = list.Color,
            Position = list.Position,
            CreatedBy = list.CreatedBy,
            CreatedAt = list.CreatedAt,
            UpdatedBy = list.UpdatedBy,
            UpdatedAt = list.UpdatedAt,
            Version = list.Version
        };

        return ApiResponse<ListDto>.Ok(dto, "List created successfully", 201);
    }

    public async Task<ApiResponse<ListDto>> UpdateListAsync(string listId, UpdateListRequest request, string currentUserId, string currentUserEmail, bool isSystemAdmin)
    {
        var list = await _listRepository.GetByIdAsync(listId);
        if (list == null)
        {
            return ApiResponse<ListDto>.Fail("List not found.", 404);
        }

        var canWrite = await _abacEvaluator.CanAccessWorkspaceAsync(currentUserId, list.WorkspaceId, ResourceAction.Write, isSystemAdmin);
        if (!canWrite)
        {
            return ApiResponse<ListDto>.Fail("Access denied.", 403);
        }

        var oldState = new TaskList
        {
            Id = list.Id,
            WorkspaceId = list.WorkspaceId,
            Name = list.Name,
            Color = list.Color,
            Position = list.Position,
            CreatedBy = list.CreatedBy,
            CreatedAt = list.CreatedAt,
            UpdatedBy = list.UpdatedBy,
            UpdatedAt = list.UpdatedAt,
            Version = list.Version,
            IsDeleted = list.IsDeleted
        };

        list.Name = request.Name.Trim();
        list.Color = request.Color;
        list.Position = request.Position;
        list.UpdatedBy = currentUserId;
        list.UpdatedAt = DateTime.UtcNow;

        await _listRepository.UpdateAsync(list);

        // Audit changelog
        await _auditService.RecordChangeAsync(
            currentUserId,
            currentUserEmail,
            "UPDATE",
            "List",
            list.Id,
            $"{currentUserEmail} modificó la lista '{list.Name}'",
            oldState,
            list);

        var dto = new ListDto
        {
            Id = list.Id,
            WorkspaceId = list.WorkspaceId,
            Name = list.Name,
            Color = list.Color,
            Position = list.Position,
            CreatedBy = list.CreatedBy,
            CreatedAt = list.CreatedAt,
            UpdatedBy = list.UpdatedBy,
            UpdatedAt = list.UpdatedAt,
            Version = list.Version + 1
        };

        return ApiResponse<ListDto>.Ok(dto, "List updated successfully");
    }

    public async Task<ApiResponse> DeleteListAsync(string listId, string currentUserId, string currentUserEmail, bool isSystemAdmin)
    {
        var list = await _listRepository.GetByIdAsync(listId);
        if (list == null)
        {
            return ApiResponse.Fail("List not found.", 404);
        }

        var canWrite = await _abacEvaluator.CanAccessWorkspaceAsync(currentUserId, list.WorkspaceId, ResourceAction.Write, isSystemAdmin);
        if (!canWrite)
        {
            return ApiResponse.Fail("Access denied.", 403);
        }

        await _listRepository.SoftDeleteAsync(listId, currentUserId);

        // Audit changelog
        await _auditService.RecordChangeAsync(
            currentUserId,
            currentUserEmail,
            "DELETE",
            "List",
            listId,
            $"{currentUserEmail} eliminó la lista '{list.Name}'",
            list,
            (TaskList?)null);

        return ApiResponse.Ok("List deleted successfully");
    }
}
