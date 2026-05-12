namespace Forge.Core.Interfaces;

/// <summary>
/// Generates and validates the magic-link tokens that gate customer-portal
/// access. Magic-link tokens are short-lived (15 minutes) one-time strings
/// that the portal exchanges for a JWT. Implementations hash the link
/// token at rest so a database leak doesn't yield usable credentials.
/// </summary>
public interface IPortalAuthService
{
    /// <summary>Generates a fresh random magic-link token + its SHA-256 hash for storage.</summary>
    (string PlaintextToken, string Hash) GenerateMagicLinkToken();

    /// <summary>Hashes a plaintext token for comparison against the stored hash.</summary>
    string HashToken(string plaintextToken);
}
