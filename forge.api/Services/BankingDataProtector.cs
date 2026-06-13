using Microsoft.AspNetCore.DataProtection;

namespace Forge.Api.Services;

/// <summary>
/// IDataProtector wrapper scoped to the "Forge.Banking" purpose (mirrors <see cref="PiiProtector"/> /
/// TokenEncryptionService). Vendor routing/account numbers are sealed with this and decrypted at
/// exactly one seam — NACHA file generation — so the plaintext never reaches a response model.
/// </summary>
public class BankingDataProtector : IBankingDataProtector
{
    private const string ProtectorPurpose = "Forge.Banking";
    private readonly IDataProtector _protector;

    public BankingDataProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(ProtectorPurpose);
    }

    public string? Protect(string? plaintext) =>
        string.IsNullOrWhiteSpace(plaintext) ? null : _protector.Protect(plaintext);

    public string? Unprotect(string? ciphertext) =>
        string.IsNullOrWhiteSpace(ciphertext) ? null : _protector.Unprotect(ciphertext);
}
