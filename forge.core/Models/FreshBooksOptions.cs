namespace Forge.Core.Models;

public class FreshBooksOptions
{
    public const string SectionName = "FreshBooks";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string AuthorizationEndpoint { get; } = "https://auth.freshbooks.com/oauth/authorize";
    public string TokenEndpoint { get; } = "https://api.freshbooks.com/auth/oauth/token";
    public string BaseApiUrl { get; } = "https://api.freshbooks.com";
}
