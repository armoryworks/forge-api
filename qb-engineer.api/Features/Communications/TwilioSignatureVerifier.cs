using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Options;

using QBEngineer.Core.Models;

namespace QBEngineer.Api.Features.Communications;

/// <summary>
/// Wave 8 — Twilio webhook signature verifier per
/// https://www.twilio.com/docs/usage/webhooks/webhooks-security.
///
/// Algorithm:
///   1. Take the full request URL (including query string).
///   2. If the body is x-www-form-urlencoded, append each key+value
///      sorted alphabetically by key (no separators).
///   3. HMAC-SHA1 with the AuthToken as key.
///   4. Base64-encode the digest.
///   5. Compare to the X-Twilio-Signature header (case-sensitive, exact match).
///
/// JSON-bodied requests: Twilio also publishes a sha256-of-body header
/// instead, but the voice status callback uses form-urlencoded so we
/// only handle that path here.
/// </summary>
public interface ITwilioSignatureVerifier
{
    /// <summary>
    /// Returns true when verification passes OR when no AuthToken is
    /// configured (dev / mock posture). Returns false ONLY when an
    /// AuthToken is present and the signature does not match.
    /// </summary>
    bool Verify(string fullUrl, IReadOnlyDictionary<string, string> formFields, string? signatureHeader);

    /// <summary>True when an auth token is configured; controls whether failures should hard-reject.</summary>
    bool IsConfigured { get; }
}

public class TwilioSignatureVerifier(IOptions<TwilioOptions> options) : ITwilioSignatureVerifier
{
    private readonly TwilioOptions _options = options.Value;

    public bool IsConfigured => !string.IsNullOrEmpty(_options.AuthToken);

    public bool Verify(string fullUrl, IReadOnlyDictionary<string, string> formFields, string? signatureHeader)
    {
        if (!IsConfigured)
        {
            // Dev / mock posture — accept anything.
            return true;
        }

        if (string.IsNullOrEmpty(signatureHeader))
        {
            return false;
        }

        var expected = ComputeSignature(fullUrl, formFields, _options.AuthToken!);
        // Constant-time compare to avoid timing-attack leakage.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(signatureHeader));
    }

    internal static string ComputeSignature(
        string fullUrl,
        IReadOnlyDictionary<string, string> formFields,
        string authToken)
    {
        var sorted = formFields.OrderBy(kvp => kvp.Key, StringComparer.Ordinal);
        var sb = new StringBuilder(fullUrl);
        foreach (var kvp in sorted)
        {
            sb.Append(kvp.Key);
            sb.Append(kvp.Value);
        }

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(authToken));
        var digest = hmac.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToBase64String(digest);
    }
}
