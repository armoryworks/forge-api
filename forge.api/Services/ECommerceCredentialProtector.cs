using Microsoft.AspNetCore.DataProtection;

namespace Forge.Api.Services;

/// <summary>
/// Storefront / marketplace credential seam (own purpose, isolated from the
/// Banking / PII / EDI / OAuth domains).
///
/// <para><b>Why this exists.</b> <c>ECommerceIntegration.EncryptedCredentials</c>
/// was named for an encryption that never happened — both the create and update
/// handlers assigned the caller's plaintext straight onto the column. That was
/// survivable while nothing could actually connect; it stops being survivable
/// now that real connectors exist and the column holds live Shopify admin tokens
/// and WooCommerce consumer secrets, which grant full read/write on someone's
/// storefront.</para>
/// </summary>
public interface IECommerceCredentialProtector
{
    string? Protect(string? plaintext);

    /// <summary>
    /// Reverses <see cref="Protect"/>. Returns the input unchanged when it is
    /// not protected payload, so rows written before this seam existed keep
    /// working rather than throwing on every poll — the value re-protects on
    /// the next save. Remove the fallback once no legacy plaintext remains.
    /// </summary>
    string? Unprotect(string? ciphertext);
}

/// <summary>IDataProtector wrapper scoped to "Forge.ECommerce" (mirrors <see cref="EdiCredentialProtector"/>).</summary>
public class ECommerceCredentialProtector(IDataProtectionProvider provider, ILogger<ECommerceCredentialProtector> logger)
    : IECommerceCredentialProtector
{
    private const string ProtectorPurpose = "Forge.ECommerce";

    private readonly IDataProtector _protector = provider.CreateProtector(ProtectorPurpose);

    public string? Protect(string? plaintext) =>
        string.IsNullOrWhiteSpace(plaintext) ? null : _protector.Protect(plaintext);

    public string? Unprotect(string? ciphertext)
    {
        if (string.IsNullOrWhiteSpace(ciphertext)) return null;

        try
        {
            return _protector.Unprotect(ciphertext);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Pre-seam plaintext, or a payload from a different key ring. Log at
            // warning so the migration is visible, and hand back what we have so
            // an existing integration keeps polling instead of hard-failing.
            logger.LogWarning(
                "[ECOMMERCE-CREDS] Stored credential is not protected payload — treating it as legacy " +
                "plaintext. It will be protected on the next save of this integration.");
            return ciphertext;
        }
    }
}
