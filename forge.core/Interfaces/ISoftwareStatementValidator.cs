namespace Forge.Core.Interfaces;

/// <summary>
/// Validates an RFC 7591 <c>software_statement</c> JWT presented during dynamic client registration.
/// The default implementation rejects every statement; swap in a real JWKS-backed validator when
/// publisher trust is configured.
/// </summary>
public interface ISoftwareStatementValidator
{
    /// <summary>
    /// Verifies the JWT's signature against a known publisher key whose <c>kid</c> is in
    /// <paramref name="trustedKeyIds"/>, checks <c>exp</c>/<c>nbf</c>, and returns the parsed claims
    /// (flat dictionary) on success. Returns <c>null</c> on any failure.
    /// </summary>
    Task<SoftwareStatementClaims?> ValidateAsync(
        string jwt,
        IReadOnlyCollection<string> trustedKeyIds,
        CancellationToken ct = default);
}

/// <summary>Parsed claims extracted from a valid software statement.</summary>
public record SoftwareStatementClaims(
    string PublisherKeyId,
    string? ClientName,
    IReadOnlyCollection<string> RedirectUris,
    IReadOnlyDictionary<string, string> ExtraClaims);
