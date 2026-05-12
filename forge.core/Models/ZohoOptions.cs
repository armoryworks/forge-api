namespace Forge.Core.Models;

public class ZohoOptions
{
    public const string SectionName = "Zoho";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string DataCenter { get; set; } = "com"; // com, eu, in, com.au, jp
    public string OrganizationId { get; set; } = string.Empty;
    public string Scopes { get; set; } = "ZohoBooks.fullaccess.all";

    public string AuthorizationEndpoint => $"https://accounts.zoho.{DataCenter}/oauth/v2/auth";
    public string TokenEndpoint => $"https://accounts.zoho.{DataCenter}/oauth/v2/token";
    public string BaseApiUrl => $"https://www.zohoapis.{DataCenter}/books/v3";
}
