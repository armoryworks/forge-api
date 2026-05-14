
namespace Forge.Core.Models;

public record CreditStatusResponseModel
{
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public decimal? CreditLimit { get; init; }
    public decimal OpenArBalance { get; init; }
    public decimal PendingOrdersTotal { get; init; }
    public decimal TotalExposure { get; init; }
    public decimal AvailableCredit { get; init; }
    public decimal UtilizationPercent { get; init; }
    public bool IsOnHold { get; init; }
    public string? HoldReason { get; init; }
    public bool IsOverLimit { get; init; }
    public CreditRisk RiskLevel { get; init; }

    // Phase 3 / WU-14 / H3 / P4-OVERPAY — sum of unapplied portions of customer
    // payments. Phase 1 found this was tracked on Payment.UnappliedAmount
    // (computed from Amount - sum(applications)) but not surfaced anywhere
    // a salesperson could read it during a phone call. Now it is.
    public decimal UnappliedCreditAmount { get; init; }
    public List<UnappliedCreditDetail> UnappliedCredits { get; init; } = [];
}

// Per-payment breakdown of unapplied credit. The Amount field is the
// unapplied portion only (Payment.Amount - sum of PaymentApplications),
// not the gross payment amount.
public record UnappliedCreditDetail(
    int PaymentId,
    string PaymentNumber,
    DateTimeOffset Date,
    decimal Amount,
    string? Reference);
