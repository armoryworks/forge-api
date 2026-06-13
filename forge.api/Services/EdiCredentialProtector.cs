using Microsoft.AspNetCore.DataProtection;

namespace Forge.Api.Services;

/// <summary>EDI transport credentials seam (own purpose, isolated from Banking/PII/OAuth domains).</summary>
public interface IEdiCredentialProtector
{
    string? Protect(string? plaintext);
    string? Unprotect(string? ciphertext);
}

/// <summary>IDataProtector wrapper scoped to "Forge.EdiTransport" (mirrors BankingDataProtector).</summary>
public class EdiCredentialProtector : IEdiCredentialProtector
{
    private const string ProtectorPurpose = "Forge.EdiTransport";
    private readonly IDataProtector _protector;

    public EdiCredentialProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(ProtectorPurpose);
    }

    public string? Protect(string? plaintext) =>
        string.IsNullOrWhiteSpace(plaintext) ? null : _protector.Protect(plaintext);

    public string? Unprotect(string? ciphertext) =>
        string.IsNullOrWhiteSpace(ciphertext) ? null : _protector.Unprotect(ciphertext);
}
