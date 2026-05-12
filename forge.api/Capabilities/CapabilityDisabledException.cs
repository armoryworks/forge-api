namespace Forge.Api.Capabilities;

/// <summary>
/// Phase 4 Phase-H — Thrown by <see cref="CapabilityGateBehavior{TRequest, TResponse}"/>
/// when a MediatR request type carries <see cref="RequiresCapabilityAttribute"/>
/// and the named capability is disabled in the current snapshot.
///
/// The MediatR pipeline runs both inside controllers (where the
/// <see cref="CapabilityGateMiddleware"/> would normally short-circuit first)
/// and outside HTTP context (Hangfire jobs, SignalR hubs, etc.). For the
/// outside-HTTP path the global exception middleware translates this to a
/// 403 envelope on a controller surface; for Hangfire the exception bubbles
/// to the job runner, which records the failure per its retry policy.
///
/// Carries the same envelope shape as the middleware's 403 response so
/// downstream HTTP consumers see a consistent payload regardless of which
/// gate fired.
/// </summary>
public sealed class CapabilityDisabledException : Exception
{
    public CapabilityDisabledException(string capability)
        : base($"Capability {capability} is disabled for this installation.")
    {
        Capability = capability;
    }

    public string Capability { get; }

    /// <summary>
    /// Renders the same error envelope shape <see cref="CapabilityGateMiddleware"/>
    /// emits, so the client-side <c>HttpErrorInterceptor</c> handles the 403
    /// from either path identically.
    /// </summary>
    public object ToEnvelope() => new
    {
        errors = new[]
        {
            new
            {
                code = "capability-disabled",
                capability = Capability,
                message = Message,
            },
        },
    };
}
