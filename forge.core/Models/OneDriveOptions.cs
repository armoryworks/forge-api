namespace Forge.Core.Models;

/// <summary>
/// Pro Services rollout — OneDrive / Microsoft Graph OAuth + API
/// configuration. Lives under <c>OneDrive</c> key in
/// <c>appsettings.json</c>:
/// <code>
/// "OneDrive": {
///   "ClientId": "...",
///   "ClientSecret": "...",
///   "TenantId": "common",
///   "Scopes": "Files.ReadWrite offline_access"
/// }
/// </code>
///
/// <para>When ClientId is empty/null, the DI registration skips the real
/// provider per Program.cs.</para>
///
/// <para>TenantId = "common" supports multi-tenant + personal Microsoft
/// accounts. For single-tenant apps (one organization), set this to the
/// tenant ID (a GUID) — required when using Files.ReadWrite.All scope.</para>
/// </summary>
public class OneDriveOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Azure AD tenant identifier. "common" for multi-tenant + personal; otherwise a tenant GUID.</summary>
    public string TenantId { get; set; } = "common";

    /// <summary>
    /// OAuth scope(s), space-separated. Default <c>Files.ReadWrite offline_access</c>
    /// grants read/write to a user's OneDrive and a refresh token.
    /// </summary>
    public string Scopes { get; set; } = "Files.ReadWrite offline_access";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
