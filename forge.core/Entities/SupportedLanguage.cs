namespace Forge.Core.Entities;

public class SupportedLanguage : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NativeName { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal CompletionPercent { get; set; }
}
