using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Options;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Services;

/// <summary>
/// Verifies first-run activation codes against the configured
/// <c>Setup:ActivationCodeHash</c>. The verifier is <c>v1.{salt}.{sha256hex}</c>,
/// written into the install's environment by whoever provisioned it (for AWT-hosted
/// tenants, Tuyere's fleet provisioner). The code itself is never stored here — the
/// customer gets it from their contact at Forge.
///
/// Codes are normalized before hashing: upper-cased, separators dropped, and the
/// look-alikes the alphabet omits folded onto their digits (I/L to 1, O to 0), so a
/// code read aloud over a phone survives transcription.
/// </summary>
public sealed class SetupActivationGuard(IOptions<SetupOptions> options) : ISetupActivationGuard
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private readonly string _configuredHash = options.Value.ActivationCodeHash?.Trim() ?? "";

    public bool IsRequired => _configuredHash.Length > 0;

    public bool Verify(string? code)
    {
        if (!IsRequired) return true;
        if (string.IsNullOrWhiteSpace(code)) return false;

        var parts = _configuredHash.Split('.');
        if (parts.Length != 3 || parts[0] != "v1") return false;

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(parts[1] + Normalize(code)));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Convert.ToHexString(digest).ToLowerInvariant()),
            Encoding.UTF8.GetBytes(parts[2]));
    }

    private static string Normalize(string code)
    {
        var buffer = new StringBuilder(code.Length);
        foreach (var raw in code)
        {
            var c = char.ToUpperInvariant(raw);
            c = c switch { 'I' or 'L' => '1', 'O' => '0', _ => c };
            if (Alphabet.Contains(c)) buffer.Append(c);
        }
        return buffer.ToString();
    }
}
