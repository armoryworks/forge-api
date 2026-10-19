using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Forge.Api.Features.ComplianceForms;

/// <summary>
/// Verifies the <c>X-Docuseal-Signature</c> header. DocuSeal sends
/// <c>&lt;unix-seconds&gt;.&lt;hex&gt;</c>, an HMAC-SHA256 over
/// <c>&lt;unix-seconds&gt;.&lt;raw body&gt;</c> keyed with the webhook secret, and
/// requests outside <see cref="ToleranceSeconds"/> of now are refused. The older
/// body-only form (<c>&lt;hex&gt;</c> or <c>sha256=&lt;hex&gt;</c>) is still accepted.
/// Hex is compared case-insensitively in constant time.
/// </summary>
public static class DocuSealWebhookSignature
{
    public const int ToleranceSeconds = 300;

    public static bool IsValid(string secret, string body, string? header, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(header))
            return false;

        var provided = header.Trim();
        var key = Encoding.UTF8.GetBytes(secret);

        var dot = provided.IndexOf('.');
        if (dot > 0)
        {
            var timestampText = provided[..dot];
            if (!long.TryParse(timestampText, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp))
                return false;
            if (Math.Abs(now.ToUnixTimeSeconds() - timestamp) > ToleranceSeconds)
                return false;

            var signed = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{timestampText}.{body}"));
            return Matches(signed, provided[(dot + 1)..]);
        }

        if (provided.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            provided = provided["sha256=".Length..];

        return Matches(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(body)), provided);
    }

    private static bool Matches(byte[] expected, string providedHex)
    {
        byte[] provided;
        try
        {
            provided = Convert.FromHexString(providedHex);
        }
        catch (FormatException)
        {
            return false;
        }

        return provided.Length == expected.Length
            && CryptographicOperations.FixedTimeEquals(expected, provided);
    }
}
