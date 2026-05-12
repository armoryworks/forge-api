namespace Forge.Core.Models;

/// <summary>
/// Pro Services rollout — Dropbox OAuth + API configuration. Lives under
/// <c>Dropbox</c> key in <c>appsettings.json</c>:
/// <code>
/// "Dropbox": {
///   "AppKey": "...",
///   "AppSecret": "...",
///   "TokenAccessType": "offline"
/// }
/// </code>
///
/// <para>When AppKey is empty/null, the DI registration skips the real
/// provider per Program.cs.</para>
///
/// <para><c>TokenAccessType=offline</c> issues a refresh token alongside
/// the access token (recommended for long-lived integrations). Set to
/// <c>online</c> for short-lived single-session tokens.</para>
/// </summary>
public class DropboxOptions
{
    /// <summary>Dropbox app key from the Dropbox developer console.</summary>
    public string AppKey { get; set; } = string.Empty;

    /// <summary>Dropbox app secret from the Dropbox developer console.</summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>"offline" (default — issues refresh tokens) or "online".</summary>
    public string TokenAccessType { get; set; } = "offline";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AppKey) && !string.IsNullOrWhiteSpace(AppSecret);
}
