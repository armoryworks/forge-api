namespace Forge.Core.Models;

public class SageOptions
{
    public const string SectionName = "Sage";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string CountryCode { get; set; } = "US";
    public string AuthorizationEndpoint { get; } = "https://www.sageone.com/oauth2/auth/central";
    public string TokenEndpoint { get; } = "https://oauth.accounting.sage.com/token";
    public string BaseApiUrl { get; } = "https://api.accounting.sage.com/v3.1";
}
