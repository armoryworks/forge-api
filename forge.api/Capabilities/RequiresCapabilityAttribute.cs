namespace Forge.Api.Capabilities;

/// <summary>
/// Phase 4 Phase-A — marks a controller / action / endpoint as gated by a
/// specific capability code. The <see cref="CapabilityGateMiddleware"/>
/// reads this metadata; if the capability is disabled in the snapshot the
/// request short-circuits with 403 + WU-02 envelope.
///
/// Phase A wires the attribute but does NOT yet apply it to any production
/// endpoint — Phase B is the slice where the first ~10 capabilities get
/// gated.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequiresCapabilityAttribute(string capability) : Attribute
{
    public string Capability { get; } = capability;
}

/// <summary>
/// Phase 4 Phase-A — marks an endpoint as exempt from capability gating
/// (the bootstrap-exempt admin surface, descriptor reads, etc.). Per 4D §3.5
/// this is intentionally its own attribute so a code reviewer scanning for
/// "what bypasses capability gating?" gets a single grep result.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class CapabilityBootstrapAttribute : Attribute
{
}
