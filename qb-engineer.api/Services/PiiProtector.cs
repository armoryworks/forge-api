using Microsoft.AspNetCore.DataProtection;

namespace QBEngineer.Api.Services;

/// <summary>
/// IDataProtector wrapper scoped to the "QbEngineer.Pii" purpose. Separating
/// the purpose string from QbEngineer.OAuthTokens (TokenEncryptionService)
/// means a leaked-key incident scoped to one domain doesn't let an attacker
/// pivot to ciphertext from the other.
/// </summary>
public class PiiProtector : IPiiProtector
{
    private const string ProtectorPurpose = "QbEngineer.Pii";
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
