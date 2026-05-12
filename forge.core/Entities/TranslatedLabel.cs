namespace Forge.Core.Entities;

public class TranslatedLabel : BaseEntity
{
    public string Key { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Context { get; set; }
    public bool IsApproved { get; set; } = true;
    public int? TranslatedById { get; set; }
    public DateTimeOffset? TranslatedAt { get; set; }
}
