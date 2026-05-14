
namespace Forge.Core.Models;

public record UpdateEdiTradingPartnerRequestModel
{
    public string? Name { get; init; }
    public int? CustomerId { get; init; }
    public int? VendorId { get; init; }
    public string? QualifierId { get; init; }
    public string? QualifierValue { get; init; }
    public string? InterchangeSenderId { get; init; }
    public string? InterchangeReceiverId { get; init; }
    public string? ApplicationSenderId { get; init; }
    public string? ApplicationReceiverId { get; init; }
    public EdiFormat? DefaultFormat { get; init; }
    public EdiTransportMethod? TransportMethod { get; init; }
    public string? TransportConfigJson { get; init; }
    public bool? AutoProcess { get; init; }
    public bool? RequireAcknowledgment { get; init; }
    public bool? IsActive { get; init; }
    public string? Notes { get; init; }
}
