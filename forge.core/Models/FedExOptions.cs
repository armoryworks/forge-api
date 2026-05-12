namespace Forge.Core.Models;

public class FedExOptions
{
    public const string SectionName = "FedEx";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";

    public string BaseUrl => Environment == "production"
        ? "https://apis.fedex.com"
        : "https://apis-sandbox.fedex.com";
}
