namespace QBEngineer.Core.Models;

/// <summary>
/// Wave 8 — Twilio integration config. Bound from <c>Twilio</c> section of
/// appsettings.json. <see cref="AuthToken"/> drives the X-Twilio-Signature
/// HMAC verification on incoming webhooks; when null we skip the check
/// (dev / local smoke-test mode).
/// </summary>
public class TwilioOptions
{
    /// <summary>Account SID — informational, not used by inbound webhook validation.</summary>
    public string? AccountSid { get; set; }

    /// <summary>
    /// Twilio auth token. When configured, inbound webhook signature is
    /// HMAC-SHA1-validated against the X-Twilio-Signature header per
    /// https://www.twilio.com/docs/usage/webhooks/webhooks-security.
    /// Leave null in dev to skip validation (matches the
    /// MockIntegrations posture other adapters take).
    /// </summary>
    public string? AuthToken { get; set; }

    /// <summary>
    /// When true, signature verification failures REJECT the webhook (403).
    /// When false (default), failures are logged but the webhook still
    /// processes — useful during initial Twilio setup when the URL might
    /// be misconfigured. Production deployments should flip this to true.
    /// </summary>
    public bool RequireSignature { get; set; }
}
