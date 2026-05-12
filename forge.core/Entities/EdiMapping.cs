namespace Forge.Core.Entities;

public class EdiMapping : BaseAuditableEntity
{
    public int TradingPartnerId { get; set; }
    public string TransactionSet { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FieldMappingsJson { get; set; } = "[]";
    public string ValueTranslationsJson { get; set; } = "[]";
    public bool IsDefault { get; set; }
    public string? Notes { get; set; }

    // Navigation
    public EdiTradingPartner TradingPartner { get; set; } = null!;
}
