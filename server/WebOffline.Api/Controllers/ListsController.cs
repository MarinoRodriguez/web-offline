using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebOffline.Core.Common;
using WebOffline.Core.DTOs;
using WebOffline.Core.Interfaces;

namespace WebOffline.Api.Controllers;

[Authorize]
[Route("api/[controller]")]
public class ListsController : BaseApiController
{
    private readonly IListService _listService;

    public ListsController(IListService listService)
    {
        _listService = listService;
    }

    [HttpGet("workspace/{workspaceId}")]
    public async Task<ActionResult<ApiResponse<IEnumerable<ListDto>>>> GetByWorkspace(string workspaceId)
    {
        var result = await _listService.GetListsByWorkspaceAsync(workspaceId, CurrentUserId, IsSystemAdmin);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ListDto>>> GetById(string id)
    {
        var result = await _listService.GetListByIdAsync(id, CurrentUserId, IsSystemAdmin);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<ListDto>>> Create([FromBody] CreateListRequest request)
    {
        var result = await _listService.CreateListAsync(request, CurrentUserId, CurrentUserEmail, IsSystemAdmin);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<ListDto>>> Update(string id, [FromBody] UpdateListRequest request)
    {
        var result = await _listService.UpdateListAsync(id, request, CurrentUserId, CurrentUserEmail, IsSystemAdmin);
        return StatusCode(result.StatusCode, result);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse>> Delete(string id)
    {
        var result = await _listService.DeleteListAsync(id, CurrentUserId, CurrentUserEmail, IsSystemAdmin);
        return StatusCode(result.StatusCode, result);
    }
}
