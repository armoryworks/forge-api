namespace Forge.Core.Models;

public class SsoProviderOptions
{
    public bool Enabled { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string? Authority { get; set; }
    public string? DisplayName { get; set; }
}
