namespace Forge.Core.Models;

public class DhlOptions
{
    public const string SectionName = "Dhl";

    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://express.api.dhl.com/mydhlapi";
}
