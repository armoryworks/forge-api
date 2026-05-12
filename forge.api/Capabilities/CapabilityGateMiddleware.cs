using System.Text.Json;

namespace Forge.Api.Capabilities;

/// <summary>
/// Phase 4 Phase-A — Capability gate middleware skeleton. Runs after auth /
/// authorization but before MVC routing/execution. Reads endpoint metadata
/// for <see cref="RequiresCapabilityAttribute"/> + <see cref="CapabilityBootstrapAttribute"/>:
///
///   • Bootstrap-marked endpoints always pass.
///   • Endpoints marked <see cref="RequiresCapabilityAttribute"/> are
///     checked against the current <see cref="CapabilitySnapshot"/>; if
///     disabled, returns 403 with the WU-02 error envelope per 4D §3.3 /
///     4D-decisions-log #1.
///   • Endpoints with no marker pass through unchanged (Phase A default —
///     no production endpoints are yet gated).
/// </summary>
public class CapabilityGateMiddleware(RequestDelegate next, ILogger<CapabilityGateMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ICapabilitySnapshotProvider snapshots)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is null)
        {
            await next(context);
            return;
        }

        // Bootstrap exemption — always allow
        if (endpoint.Metadata.GetMetadata<CapabilityBootstrapAttribute>() is not null)
        {
            await next(context);
            return;
        }

        var requires = endpoint.Metadata.GetMetadata<RequiresCapabilityAttribute>();
        if (requires is null)
        {
            await next(context);
            return;
        }

        if (snapshots.IsEnabled(requires.Capability))
        {
            await next(context);
            return;
        }

        logger.LogInformation(
            "[CAPABILITY-GATE] Capability {Capability} disabled — short-circuiting {Method} {Path}",
            requires.Capability, context.Request.Method, context.Request.Path);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers["X-Capability-Disabled"] = requires.Capability;

        var payload = new
        {
            errors = new[]
            {
                new
                {
                    code = "capability-disabled",
                    capability = requires.Capability,
                    message = "This capability is disabled for this installation.",
                },
            },
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, _json));
    }

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
