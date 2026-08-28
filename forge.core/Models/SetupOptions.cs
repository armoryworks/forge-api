namespace Forge.Core.Models;

/// <summary>
/// First-run gate for an install that was provisioned for someone rather than
/// installed by them. When <see cref="ActivationCodeHash"/> is set, the setup
/// wizard demands an activation code that hashes to it before creating the first
/// admin — so a freshly provisioned tenant belongs to the customer it was stood up
/// for, not to whoever finds the hostname first. Blank (the default, and every
/// self-hosted install) leaves the wizard open exactly as before.
/// </summary>
public class SetupOptions
{
    public const string SectionName = "Setup";

    /// <summary>Salted verifier in the form <c>v1.{salt}.{sha256(salt + code)}</c>.</summary>
    public string ActivationCodeHash { get; set; } = "";
}
