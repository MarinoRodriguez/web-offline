using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using WebOffline.Core.Entities;
using WebOffline.Core.Interfaces;

namespace WebOffline.Api.Middleware;

public class AuditLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuditLoggingMiddleware> _logger;
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

    public AuditLoggingMiddleware(RequestDelegate next, ILogger<AuditLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IAuditRepository auditRepository)
    {
        // Skip swagger/openapi/static non-api assets to avoid cluttering audit log
        var path = context.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var correlationId = context.TraceIdentifier ?? Guid.NewGuid().ToString();

        // 1. Capture Request Headers
        var requestHeadersDict = new Dictionary<string, string>();
        foreach (var header in context.Request.Headers)
        {
            if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                requestHeadersDict[header.Key] = "Bearer ***REDACTED***";
            }
            else
            {
                requestHeadersDict[header.Key] = header.Value.ToString();
            }
        }
        var requestHeadersJson = JsonSerializer.Serialize(requestHeadersDict, _jsonOptions);

        // 2. Read and buffer Request Body
        context.Request.EnableBuffering();
        string requestBody = string.Empty;

        if (context.Request.ContentLength > 0 && context.Request.Body.CanRead)
        {
            using var reader = new StreamReader(
                context.Request.Body,
                encoding: Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            requestBody = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0; // Reset for downstream handlers
        }

        var sanitizedRequestBody = SanitizePayload(requestBody);

        // 3. Intercept Response Body
        var originalResponseBodyStream = context.Response.Body;
        using var memoryResponseStream = new MemoryStream();
        context.Response.Body = memoryResponseStream;

        string responseBody = string.Empty;
        int statusCode = 500;

        try
        {
            await _next(context);
            statusCode = context.Response.StatusCode;

            memoryResponseStream.Position = 0;
            using var responseReader = new StreamReader(memoryResponseStream, Encoding.UTF8, false, 1024, leaveOpen: true);
            responseBody = await responseReader.ReadToEndAsync();
            memoryResponseStream.Position = 0;

            await memoryResponseStream.CopyToAsync(originalResponseBodyStream);
        }
        catch (Exception)
        {
            statusCode = context.Response.StatusCode;
            throw;
        }
        finally
        {
            context.Response.Body = originalResponseBodyStream;
            stopwatch.Stop();

            // 4. Capture Response Headers
            var responseHeadersDict = new Dictionary<string, string>();
            foreach (var header in context.Response.Headers)
            {
                responseHeadersDict[header.Key] = header.Value.ToString();
            }
            var responseHeadersJson = JsonSerializer.Serialize(responseHeadersDict, _jsonOptions);

            // Extract user claims if authenticated
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userEmail = context.User.FindFirst(ClaimTypes.Email)?.Value;
            var clientIp = context.Connection.RemoteIpAddress?.ToString();
            var userAgent = context.Request.Headers.UserAgent.ToString();

            var log = new HttpRequestLog
            {
                Id = Guid.NewGuid().ToString(),
                CorrelationId = correlationId,
                UserId = userId,
                UserEmail = userEmail,
                ClientIp = clientIp,
                UserAgent = userAgent,
                HttpMethod = context.Request.Method,
                Path = path,
                QueryString = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null,
                RequestHeaders = requestHeadersJson,
                RequestBody = string.IsNullOrWhiteSpace(sanitizedRequestBody) ? null : sanitizedRequestBody,
                StatusCode = statusCode,
                ResponseHeaders = responseHeadersJson,
                ResponseBody = string.IsNullOrWhiteSpace(responseBody) ? null : responseBody,
                DurationMs = stopwatch.ElapsedMilliseconds,
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                await auditRepository.LogRequestAsync(log);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write HTTP audit request log: {Message}", ex.Message);
            }
        }
    }

    private static string SanitizePayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return payload;

        return System.Text.RegularExpressions.Regex.Replace(
            payload,
            @"(""password""\s*:\s*"")[^""]*("")",
            "$1***REDACTED***$2",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
