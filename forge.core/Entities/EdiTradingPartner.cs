
namespace Forge.Core.Entities;

public class EdiTradingPartner : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public int? CustomerId { get; set; }
    public int? VendorId { get; set; }

    // EDI identifiers
    public string QualifierId { get; set; } = string.Empty;
    public string QualifierValue { get; set; } = string.Empty;
    public string? InterchangeSenderId { get; set; }
    public string? InterchangeReceiverId { get; set; }
    public string? ApplicationSenderId { get; set; }
    public string? ApplicationReceiverId { get; set; }

    // Format & transport
    public EdiFormat DefaultFormat { get; set; } = EdiFormat.X12;
    public EdiTransportMethod TransportMethod { get; set; }
    public string? TransportConfigJson { get; set; }

    // Processing rules
    public bool AutoProcess { get; set; } = true;
    public bool RequireAcknowledgment { get; set; } = true;
    public string? DefaultMappingProfileId { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public int? TestModePartnerId { get; set; }

    // Navigation
    public Customer? Customer { get; set; }
    public Vendor? Vendor { get; set; }
    public ICollection<EdiTransaction> Transactions { get; set; } = [];
    public ICollection<EdiMapping> Mappings { get; set; } = [];
}
