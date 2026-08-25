using System.Security.Cryptography;
using System.Text;

namespace Forge.Api.Services;

/// <summary>
/// Opaque bearer credentials (enrollment + refresh tokens): 32 bytes of CSPRNG
/// output, base64url on the wire, only the SHA-256 hex digest at rest.
/// </summary>
public static class OpaqueTokens
{
    public static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static string Sha256Hex(string raw)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexStringLower(digest);
    }
}
