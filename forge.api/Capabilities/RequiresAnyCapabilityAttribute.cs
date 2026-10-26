namespace Forge.Api.Capabilities;

/// <summary>
/// Marks a controller / action / MediatR request as gated by a set of capability codes of which
/// <b>any one</b> enabled lets the request through. The <see cref="CapabilityGateMiddleware"/> and the
/// <see cref="Behaviors.CapabilityGateBehavior{TRequest, TResponse}"/> read it beside
/// <see cref="RequiresCapabilityAttribute"/>; when every listed capability is disabled the request is
/// refused with the same 403 envelope, naming the first code.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequiresAnyCapabilityAttribute(params string[] capabilities) : Attribute
{
    public IReadOnlyList<string> Capabilities { get; } = capabilities;
}
