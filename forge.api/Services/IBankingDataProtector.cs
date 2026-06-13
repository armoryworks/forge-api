namespace Forge.Api.Services;

/// <summary>
/// Field-level encryption seam for vendor banking details (BANK-002). Scoped to its own
/// Data-Protection purpose so banking ciphertext is cryptographically isolated from the
/// PII and OAuth-token domains.
/// </summary>
public interface IBankingDataProtector
{
    string? Protect(string? plaintext);
    string? Unprotect(string? ciphertext);
}
