namespace QBEngineer.Core.Models.Communications;

/// <summary>
/// Wave 8 phase 1k.2 — bound from <c>OAuthImap</c> section of appsettings.
/// One sub-section per supported provider. When a provider's <c>ClientId</c>
/// is empty/null the OAuth flow for that provider is disabled (the UI
/// hides the catalog entry); fill them in per environment to enable.
///
/// Redirect URI is shared across providers and points at the SPA's
/// callback page, which posts the code+state back to the server's
/// <c>/oauth/imap/{provider}/callback</c> endpoint.
/// </summary>
public class OAuthImapOptions
{
    public OAuthProviderCredentials Google { get; set; } = new();
    public OAuthProviderCredentials Microsoft { get; set; } = new();

    /// <summary>
    /// Public-facing redirect URI registered with each OAuth provider's
    /// developer console. Format: <c>https://your-domain/account/communications/oauth-callback</c>
    /// (the SPA route that handles the redirect-back). Both Google and
    /// Microsoft must have this exact URI registered as an authorized
    /// redirect; mismatches surface as <c>redirect_uri_mismatch</c> at
    /// the authorize step.
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;
}

public class OAuthProviderCredentials
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret);
}
