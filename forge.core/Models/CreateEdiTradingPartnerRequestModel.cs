
namespace Forge.Core.Models;

public record CreateEdiTradingPartnerRequestModel
{
    public string Name { get; init; } = string.Empty;
    public int? CustomerId { get; init; }
    public int? VendorId { get; init; }
    public string QualifierId { get; init; } = string.Empty;
    public string QualifierValue { get; init; } = string.Empty;
    public string? InterchangeSenderId { get; init; }
    public string? InterchangeReceiverId { get; init; }
    public string? ApplicationSenderId { get; init; }
    public string? ApplicationReceiverId { get; init; }
    public EdiFormat DefaultFormat { get; init; } = EdiFormat.X12;
    public EdiTransportMethod TransportMethod { get; init; }
    public string? TransportConfigJson { get; init; }
    public bool AutoProcess { get; init; } = true;
    public bool RequireAcknowledgment { get; init; } = true;
    public string? Notes { get; init; }
}
