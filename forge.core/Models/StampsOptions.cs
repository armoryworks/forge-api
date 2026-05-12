namespace Forge.Core.Models;

public class StampsOptions
{
    public const string SectionName = "Stamps";

    public string ApiKey { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
}
