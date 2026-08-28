namespace Forge.Core.Interfaces;

/// <summary>
/// Decides whether this install's first-run wizard is gated on an activation code,
/// and checks codes against the configured verifier. Ungated installs (no verifier
/// configured) report <see cref="IsRequired"/> false and accept anything.
/// </summary>
public interface ISetupActivationGuard
{
    /// <summary>True when an activation code is configured and must be supplied.</summary>
    bool IsRequired { get; }

    /// <summary>True when the code matches, or when the install is ungated.</summary>
    bool Verify(string? code);
}
