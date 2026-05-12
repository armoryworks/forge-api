namespace Forge.Core.Models;

/// <summary>
/// Pro Services rollout — Google Drive OAuth + API configuration.
/// Lives under <c>GoogleDrive</c> key in <c>appsettings.json</c>:
/// <code>
/// "GoogleDrive": {
///   "ClientId": "...apps.googleusercontent.com",
///   "ClientSecret": "...",
///   "Scopes": "https://www.googleapis.com/auth/drive.file"
/// }
/// </code>
///
/// <para>When ClientId is empty/null, the DI registration falls back to
/// <c>MockCloudStorageIntegrationService</c> per
/// <c>Program.cs</c> — keeps fresh installs / dev environments running
/// without Google API credentials.</para>
/// </summary>
public class GoogleDriveOptions
{
    /// <summary>OAuth 2.0 client ID from Google Cloud Console.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OAuth 2.0 client secret from Google Cloud Console.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// OAuth scope(s), space-separated. Default <c>drive.file</c> grants per-file
    /// access (only files / folders the app creates) — preferred for least-
    /// privilege. Switch to <c>drive</c> for full Drive access.
    /// </summary>
    public string Scopes { get; set; } = "https://www.googleapis.com/auth/drive.file";

    /// <summary>True when credentials are configured (used by Program.cs to decide mock vs real).</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
