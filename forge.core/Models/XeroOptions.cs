namespace Forge.Core.Models;

public class XeroOptions
{
    public const string SectionName = "Xero";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string Scopes { get; set; } = "openid profile email accounting.transactions accounting.contacts offline_access";
    public string AuthorizationEndpoint { get; } = "https://login.xero.com/identity/connect/authorize";
    public string TokenEndpoint { get; } = "https://identity.xero.com/connect/token";
    public string BaseApiUrl { get; } = "https://api.xero.com/api.xro/2.0";
}
