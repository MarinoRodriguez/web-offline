using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebOffline.Core.Common;
using WebOffline.Core.DTOs;
using WebOffline.Core.Interfaces;

namespace WebOffline.Api.Controllers;

[Authorize]
[Route("api/[controller]")]
public class SyncController : BaseApiController
{
    private readonly ISyncService _syncService;

    public SyncController(ISyncService syncService)
    {
        _syncService = syncService;
    }

    [HttpPost("batch")]
    public async Task<ActionResult<ApiResponse<SyncBatchResponse>>> BatchSync([FromBody] SyncBatchRequest request)
    {
        var result = await _syncService.SyncBatchAsync(request, CurrentUserId, CurrentUserEmail, IsSystemAdmin);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("pull")]
    public async Task<ActionResult<ApiResponse<SyncBatchResponse>>> PullChanges(
        [FromQuery] DateTime? lastSyncedAt,
        [FromQuery] string? workspaceId)
    {
        var result = await _syncService.PullChangesAsync(lastSyncedAt, workspaceId, CurrentUserId, IsSystemAdmin);
        return StatusCode(result.StatusCode, result);
    }
}
