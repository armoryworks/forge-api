using System.Security.Cryptography;
using System.Text;

namespace Forge.Api.Features.SalesOrders.Acceptance;

/// <summary>
/// Token + second-key handling for the public accept portal. The token is the unguessable link; the
/// verification key is a shared secret the customer must also prove (e.g. their PO number) so a leaked
/// link alone can't accept. The key is only ever stored hashed.
/// </summary>
internal static class AcceptancePortalCrypto
{
    /// <summary>32 random bytes as URL-safe base64 (fits the 128-char token column comfortably).</summary>
    public static string GenerateToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>Normalizes (trim + case-fold) then SHA-256 hex. Null/blank key → null (no second factor).</summary>
    public static string? HashKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var normalized = key.Trim().ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    /// <summary>Constant-time-ish compare of a supplied key against the stored hash.</summary>
    public static bool KeyMatches(string? suppliedKey, string? storedHash)
    {
        if (string.IsNullOrEmpty(storedHash)) return true; // no second factor configured
        var supplied = HashKey(suppliedKey);
        return supplied is not null
            && CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(supplied), Encoding.ASCII.GetBytes(storedHash));
    }
}
