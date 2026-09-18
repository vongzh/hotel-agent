using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using StayOta.Agent.Abstractions.Options;

namespace StayOta.Agent.Host.Security;

/// <summary>
/// Simple shared-secret gate for API + MCP. Disabled when Hosting:ApiKey is empty (demo).
/// Accepts header <c>X-Api-Key</c> or <c>Authorization: Bearer …</c>.
/// </summary>
public sealed class ApiKeyMiddleware(RequestDelegate next, IOptions<HostingOptions> options)
{
    private static readonly PathString[] AllowAnonymous =
    [
        new("/health"),
        new("/health/ready"),
        new("/health/live")
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            await next(context);
            return;
        }

        var path = context.Request.Path;
        if (AllowAnonymous.Any(p => path.StartsWithSegments(p)))
        {
            await next(context);
            return;
        }

        // Swagger UI assets in demo; when ApiKey set, swagger still needs key or we allow only in DemoEnabled without key.
        if (opts.DemoEnabled && (path.StartsWithSegments("/swagger") || path.StartsWithSegments("/openapi")))
        {
            await next(context);
            return;
        }

        var provided = context.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(provided))
        {
            var auth = context.Request.Headers.Authorization.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                provided = auth["Bearer ".Length..].Trim();
        }

        if (!string.Equals(provided, opts.ApiKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { message = "unauthorized: missing or invalid API key" });
            return;
        }

        await next(context);
    }
}
