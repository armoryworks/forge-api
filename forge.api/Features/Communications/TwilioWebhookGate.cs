using Forge.Core.Settings;

namespace Forge.Api.Features.Communications;

/// <summary>
/// Decides whether an inbound Twilio webhook is accepted. With an auth token configured the
/// X-Twilio-Signature must verify against the URL Twilio called (<c>twilio.webhook-url</c> when
/// set, else the URL rebuilt from the request). Without a token, unsigned posts are accepted only
/// in Mock mode with <c>twilio.require-signature</c> off; every other posture rejects them.
/// </summary>
public static class TwilioWebhookGate
{
    public const string SignatureMismatch = "twilio-signature-mismatch";
    public const string SignatureNotConfigured = "twilio-signature-not-configured";

    /// <summary>Null when the webhook is accepted, otherwise the rejection code.</summary>
    public static async Task<string?> RejectReasonAsync(
        ISettingsService settings,
        ITwilioSignatureVerifier verifier,
        string requestUrl,
        string queryString,
        IReadOnlyDictionary<string, string> fields,
        string? signature,
        CancellationToken ct)
    {
        if (await verifier.IsConfiguredAsync(ct))
        {
            var signedUrl = await SignedUrlAsync(settings, requestUrl, queryString, ct);
            return await verifier.VerifyAsync(signedUrl, fields, signature, ct) ? null : SignatureMismatch;
        }

        var mode = await settings.GetStringAsync(TwilioSettings.KeyMode, ct);
        var mockMode = string.Equals(mode, IntegrationModeChoices.Mock, StringComparison.OrdinalIgnoreCase);
        var requireSignature = await settings.GetBoolAsync(TwilioSettings.KeyRequireSignature, ct);
        return mockMode && !requireSignature ? null : SignatureNotConfigured;
    }

    /// <summary>
    /// The URL the signature is computed over. The configured public URL wins because a proxy in
    /// front of the API can change the scheme, host or port Twilio actually called.
    /// </summary>
    public static async Task<string> SignedUrlAsync(
        ISettingsService settings, string requestUrl, string queryString, CancellationToken ct)
    {
        var configured = (await settings.GetStringAsync(TwilioSettings.KeyWebhookUrl, ct))?.Trim();
        if (string.IsNullOrEmpty(configured))
            return requestUrl;
        return configured.Contains('?') ? configured : configured + queryString;
    }
}
