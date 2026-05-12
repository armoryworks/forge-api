namespace Forge.Core.Entities;

public class Plant : BaseAuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int CompanyLocationId { get; set; }
    public string? TimeZone { get; set; }
    public string? CurrencyCode { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }

    public CompanyLocation Location { get; set; } = null!;
    public ICollection<InterPlantTransfer> OutboundTransfers { get; set; } = [];
    public ICollection<InterPlantTransfer> InboundTransfers { get; set; } = [];
}
