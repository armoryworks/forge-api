namespace Forge.Core.Models;

/// <summary>
/// Resolved, decrypted carrier API credentials (from the Carrier row entered via the admin UI). Generic
/// across carriers: <see cref="ClientId"/> is the key / client-id / consumer-key / api-key,
/// <see cref="Secret"/> is the decrypted secret, plus the account number and sandbox/production environment.
/// Each adapter maps this onto its own options shape.
/// </summary>
public record CarrierCredentials(
    string ClientId,
    string Secret,
    string? AccountNumber,
    string Environment);
