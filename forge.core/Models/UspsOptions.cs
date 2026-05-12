namespace Forge.Core.Models;

public class UspsOptions
{
    public const string SectionName = "Usps";

    public string ConsumerKey { get; set; } = string.Empty;
    public string ConsumerSecret { get; set; } = string.Empty;
}
