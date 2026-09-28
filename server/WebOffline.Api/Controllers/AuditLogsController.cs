using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WebOffline.Core.Common;
using WebOffline.Core.Entities;
using WebOffline.Core.Interfaces;

namespace WebOffline.Api.Controllers;

[Authorize(Roles = "admin")]
[Route("api/audit")]
public class AuditLogsController : BaseApiController
{
    private readonly IAuditRepository _auditRepository;
    private readonly IAuditService _auditService;

    public AuditLogsController(IAuditRepository auditRepository, IAuditService auditService)
    {
        _auditRepository = auditRepository;
        _auditService = auditService;
    }

    [HttpGet("requests")]
    public async Task<ActionResult<ApiResponse<object>>> GetRequestLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? userId = null,
        [FromQuery] string? path = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 50;

        var items = await _auditRepository.GetRequestLogsAsync(page, pageSize, userId, path);
        var total = await _auditRepository.CountRequestLogsAsync(userId, path);

        return Ok(ApiResponse<object>.Ok(new
        {
            Page = page,
            PageSize = pageSize,
            Total = total,
            Items = items
        }));
    }

    [HttpGet("changelog")]
    public async Task<ActionResult<ApiResponse<object>>> GetChangeLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? entityType = null,
        [FromQuery] string? entityId = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 50;

        var items = await _auditRepository.GetChangeLogsAsync(page, pageSize, entityType, entityId);
        var total = await _auditRepository.CountChangeLogsAsync(entityType, entityId);

        return Ok(ApiResponse<object>.Ok(new
        {
            Page = page,
            PageSize = pageSize,
            Total = total,
            Items = items
        }));
    }

    [HttpGet("changelog/{id}")]
    public async Task<ActionResult<ApiResponse<AuditChangeLog>>> GetChangeLogById(string id)
    {
        var log = await _auditRepository.GetChangeLogByIdAsync(id);
        if (log == null)
        {
            return NotFound(ApiResponse<AuditChangeLog>.Fail("Audit changelog entry not found.", 404));
        }

        return Ok(ApiResponse<AuditChangeLog>.Ok(log));
    }

    [HttpGet("changelog/{id}/verify")]
    public async Task<ActionResult<ApiResponse<object>>> VerifyIntegrity(string id)
    {
        var log = await _auditRepository.GetChangeLogByIdAsync(id);
        if (log == null)
        {
            return NotFound(ApiResponse<object>.Fail("Audit changelog entry not found.", 404));
        }

        var isValid = _auditService.VerifyChangeLogIntegrity(log);

        return Ok(ApiResponse<object>.Ok(new
        {
            Id = log.Id,
            EntityType = log.EntityType,
            EntityId = log.EntityId,
            Action = log.Action,
            TamperHash = log.TamperHash,
            IsValid = isValid,
            Status = isValid ? "VERIFIED_UNTOUCHED" : "TAMPERED_WARNING",
            Message = isValid 
                ? "The diffing and changelog entry are cryptographically verified and intact."
                : "ALERT: Hash mismatch! This changelog entry or diffing has been tampered with or modified."
        }));
    }
}
