namespace Forge.Core.Models;

public class StampsOptions
{
    public const string SectionName = "Stamps";

    public string ApiKey { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    /// <summary>
    /// Stamps.com SOAP API password. Captured on save so the real
    /// SwsimV111 service (not yet implemented) picks it up the moment
    /// it lands without operators having to re-enter credentials.
    /// </summary>
    public string Password { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
}
