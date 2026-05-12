using MediatR;

using Forge.Api.Capabilities;

namespace Forge.Api.Behaviors;

/// <summary>
/// Phase 4 Phase-H — MediatR pipeline behavior that mirrors the controller-side
/// <see cref="CapabilityGateMiddleware"/> for MediatR requests dispatched
/// outside an HTTP context (Hangfire jobs, SignalR hub callbacks, internal
/// wiring that fans out via <c>IMediator.Send</c>).
///
/// Reads <see cref="RequiresCapabilityAttribute"/> from the request type;
/// if present and the named capability is disabled in the
/// <see cref="ICapabilitySnapshotProvider"/> snapshot, throws
/// <see cref="CapabilityDisabledException"/>. The global exception middleware
/// translates this to a 403 envelope on the HTTP path; Hangfire records the
/// throw on the background-job path.
///
/// For controller-dispatched MediatR commands the
/// <see cref="CapabilityGateMiddleware"/> runs first and short-circuits the
/// request before this behavior fires — but applying the attribute on the
/// command is still useful as the durable / executable record of which
/// capability gates that command (the controller attribute ensures the gate
/// also fires on the HTTP edge).
/// </summary>
public sealed class CapabilityGateBehavior<TRequest, TResponse>(ICapabilitySnapshotProvider snapshots)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly RequiresCapabilityAttribute? _attribute =
        (RequiresCapabilityAttribute?)Attribute.GetCustomAttribute(
            typeof(TRequest), typeof(RequiresCapabilityAttribute));

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (_attribute is null)
            return await next();

        if (snapshots.IsEnabled(_attribute.Capability))
            return await next();

        throw new CapabilityDisabledException(_attribute.Capability);
    }
}
