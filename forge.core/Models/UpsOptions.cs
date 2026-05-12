namespace Forge.Core.Models;

public class UpsOptions
{
    public const string SectionName = "Ups";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox"; // "sandbox" or "production"

    public string BaseUrl => Environment == "production"
        ? "https://onlinetools.ups.com/api"
        : "https://wwwcie.ups.com/api";

    public string TokenUrl => Environment == "production"
        ? "https://onlinetools.ups.com/security/v1/oauth/token"
        : "https://wwwcie.ups.com/security/v1/oauth/token";
}
