using Microsoft.AspNetCore.DataProtection;

namespace Forge.Api.Services;

/// <summary>
/// IDataProtector wrapper scoped to the "Forge.Pii" purpose. Separating
/// the purpose string from Forge.OAuthTokens (TokenEncryptionService)
/// means a leaked-key incident scoped to one domain doesn't let an attacker
/// pivot to ciphertext from the other.
/// </summary>
public class PiiProtector : IPiiProtector
{
    private const string ProtectorPurpose = "Forge.Pii";
    private readonly IDataProtector _protector;

    public PiiProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(ProtectorPurpose);
    }

    public string? Protect(string? plaintext) =>
        string.IsNullOrWhiteSpace(plaintext) ? null : _protector.Protect(plaintext);

    public string? Unprotect(string? ciphertext) =>
        string.IsNullOrWhiteSpace(ciphertext) ? null : _protector.Unprotect(ciphertext);
}
