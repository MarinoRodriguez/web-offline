using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using WebOffline.Core.Common;
using WebOffline.Core.Interfaces;

namespace WebOffline.Api.Middleware;

public class SessionValidationMiddleware
{
    private readonly RequestDelegate _next;

    public SessionValidationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IUserSessionRepository sessionRepository)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var sessionId = context.User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                await ReturnUnauthorized(context, "Token invalid: Missing session identifier");
                return;
            }

            var session = await sessionRepository.GetByIdAsync(sessionId);
            if (session == null || session.IsRevoked || session.ExpiresAt <= System.DateTime.UtcNow)
            {
                await ReturnUnauthorized(context, "Session has been revoked or expired");
                return;
            }
        }

        await _next(context);
    }

    private static async Task ReturnUnauthorized(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";

        var response = ApiResponse.Fail(message, StatusCodes.Status401Unauthorized);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}
