using System.Net;
using System.Text.Json;
using Tiaano.Vms.Api.DTOs;

namespace Tiaano.Vms.Api.Middleware;

/// <summary>
/// When MustChangePassword is set, only password-change and session endpoints are allowed.
/// </summary>
public class MustChangePasswordMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly HashSet<string> AllowedPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/change-password",
        "/api/auth/me",
        "/api/auth/logout",
        "/api/auth/refresh",
        "/api/settings/branding"
    };

    public MustChangePasswordMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && context.User.HasClaim("mcp", "1")
            && context.Request.Path.StartsWithSegments("/api")
            && !AllowedPrefixes.Any(p => context.Request.Path.StartsWithSegments(p)))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            context.Response.ContentType = "application/json";
            var payload = new ApiResponse<object>(false, null, "Password change required before continuing.");
            await context.Response.WriteAsync(JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
            return;
        }

        await _next(context);
    }
}

public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(self), microphone=(), geolocation=()";
            headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self'";
            if (context.RequestServices.GetService<IHostEnvironment>()?.IsProduction() == true)
                headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
            return Task.CompletedTask;
        });

        await _next(context);
    }
}
