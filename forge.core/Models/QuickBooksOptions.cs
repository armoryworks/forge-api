namespace Forge.Core.Models;

public class QuickBooksOptions
{
    public const string SectionName = "QuickBooks";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string RedirectUri { get; set; } = string.Empty;
    public string SandboxCompanyId { get; set; } = string.Empty;

    public string AuthorizationEndpoint => Environment == "production"
        ? "https://appcenter.intuit.com/connect/oauth2"
        : "https://appcenter.intuit.com/connect/oauth2";

    public string TokenEndpoint => Environment == "production"
        ? "https://oauth.platform.intuit.com/oauth2/v1/tokens/bearer"
        : "https://oauth.platform.intuit.com/oauth2/v1/tokens/bearer";

    public string RevokeEndpoint => Environment == "production"
        ? "https://developer.api.intuit.com/v2/oauth2/tokens/revoke"
        : "https://developer.api.intuit.com/v2/oauth2/tokens/revoke";

    public string BaseApiUrl => Environment == "production"
        ? "https://quickbooks.api.intuit.com"
        : "https://sandbox-quickbooks.api.intuit.com";

    public string Scopes => "com.intuit.quickbooks.accounting";
}
