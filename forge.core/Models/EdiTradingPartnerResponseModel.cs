using Forge.Core.Enums;

namespace Forge.Core.Models;

public record EdiTradingPartnerResponseModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int? CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public int? VendorId { get; init; }
    public string? VendorName { get; init; }
    public string QualifierId { get; init; } = string.Empty;
    public string QualifierValue { get; init; } = string.Empty;
    public EdiFormat DefaultFormat { get; init; }
    public EdiTransportMethod TransportMethod { get; init; }
    public bool AutoProcess { get; init; }
    public bool RequireAcknowledgment { get; init; }
    public bool IsActive { get; init; }
    public string? Notes { get; init; }
    public int TransactionCount { get; init; }
    public DateTimeOffset? LastTransactionAt { get; init; }
    public int ErrorCount { get; init; }
}
