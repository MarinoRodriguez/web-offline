using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WebOffline.Api.Security.Abac;
using WebOffline.Core.Common;
using WebOffline.Core.DTOs;
using WebOffline.Core.Entities;
using WebOffline.Core.Interfaces;

namespace WebOffline.Api.Controllers;

[Authorize]
[Route("api/[controller]")]
public class ListsController : BaseApiController
{
    private readonly IListRepository _listRepository;
    private readonly IAbacPolicyEvaluator _abacEvaluator;

    public ListsController(
        IListRepository listRepository,
        IAbacPolicyEvaluator abacEvaluator)
    {
        _listRepository = listRepository;
        _abacEvaluator = abacEvaluator;
    }

    [HttpGet("workspace/{workspaceId}")]
    public async Task<ActionResult<ApiResponse<IEnumerable<ListDto>>>> GetByWorkspace(string workspaceId)
    {
        var canRead = await _abacEvaluator.CanAccessWorkspaceAsync(CurrentUserId, workspaceId, ResourceAction.Read, IsSystemAdmin);
        if (!canRead)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<IEnumerable<ListDto>>.Fail("Access denied.", 403));
        }

        var lists = await _listRepository.GetByWorkspaceIdAsync(workspaceId);
        var dtos = lists.Select(l => new ListDto
        {
            Id = l.Id,
            WorkspaceId = l.WorkspaceId,
            Name = l.Name,
            Color = l.Color,
            Position = l.Position,
            CreatedAt = l.CreatedAt,
            UpdatedAt = l.UpdatedAt,
            Version = l.Version
        });

        return Ok(ApiResponse<IEnumerable<ListDto>>.Ok(dtos));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ListDto>>> GetById(string id)
    {
        var list = await _listRepository.GetByIdAsync(id);
        if (list == null)
        {
            return NotFound(ApiResponse<ListDto>.Fail("List not found.", 404));
        }

        var canRead = await _abacEvaluator.CanAccessWorkspaceAsync(CurrentUserId, list.WorkspaceId, ResourceAction.Read, IsSystemAdmin);
        if (!canRead)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<ListDto>.Fail("Access denied.", 403));
        }

        var dto = new ListDto
        {
            Id = list.Id,
            WorkspaceId = list.WorkspaceId,
            Name = list.Name,
            Color = list.Color,
            Position = list.Position,
            CreatedAt = list.CreatedAt,
            UpdatedAt = list.UpdatedAt,
            Version = list.Version
        };

        return Ok(ApiResponse<ListDto>.Ok(dto));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<ListDto>>> Create([FromBody] CreateListRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.WorkspaceId) || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(ApiResponse<ListDto>.Fail("WorkspaceId and Name are required."));
        }

        var canWrite = await _abacEvaluator.CanAccessWorkspaceAsync(CurrentUserId, request.WorkspaceId, ResourceAction.Write, IsSystemAdmin);
        if (!canWrite)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<ListDto>.Fail("Access denied: You need editor permissions in this workspace.", 403));
        }

        var list = new TaskList
        {
            Id = Guid.NewGuid().ToString(),
            WorkspaceId = request.WorkspaceId,
            Name = request.Name.Trim(),
            Color = string.IsNullOrWhiteSpace(request.Color) ? "#3B82F6" : request.Color,
            Position = request.Position,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1,
            IsDeleted = false
        };

        await _listRepository.CreateAsync(list);

        var dto = new ListDto
        {
            Id = list.Id,
            WorkspaceId = list.WorkspaceId,
            Name = list.Name,
            Color = list.Color,
            Position = list.Position,
            CreatedAt = list.CreatedAt,
            UpdatedAt = list.UpdatedAt,
            Version = list.Version
        };

        return CreatedAtAction(nameof(GetById), new { id = list.Id }, ApiResponse<ListDto>.Ok(dto, "List created successfully", 201));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<ListDto>>> Update(string id, [FromBody] UpdateListRequest request)
    {
        var list = await _listRepository.GetByIdAsync(id);
        if (list == null)
        {
            return NotFound(ApiResponse<ListDto>.Fail("List not found.", 404));
        }

        var canWrite = await _abacEvaluator.CanAccessWorkspaceAsync(CurrentUserId, list.WorkspaceId, ResourceAction.Write, IsSystemAdmin);
        if (!canWrite)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<ListDto>.Fail("Access denied.", 403));
        }

        list.Name = request.Name.Trim();
        list.Color = request.Color;
        list.Position = request.Position;

        await _listRepository.UpdateAsync(list);

        var dto = new ListDto
        {
            Id = list.Id,
            WorkspaceId = list.WorkspaceId,
            Name = list.Name,
            Color = list.Color,
            Position = list.Position,
            CreatedAt = list.CreatedAt,
            UpdatedAt = DateTime.UtcNow,
            Version = list.Version + 1
        };

        return Ok(ApiResponse<ListDto>.Ok(dto, "List updated successfully"));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse>> Delete(string id)
    {
        var list = await _listRepository.GetByIdAsync(id);
        if (list == null)
        {
            return NotFound(ApiResponse.Fail("List not found.", 404));
        }

        var canWrite = await _abacEvaluator.CanAccessWorkspaceAsync(CurrentUserId, list.WorkspaceId, ResourceAction.Write, IsSystemAdmin);
        if (!canWrite)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail("Access denied.", 403));
        }

        await _listRepository.SoftDeleteAsync(id);
        return Ok(ApiResponse.Ok("List deleted successfully"));
    }
}
