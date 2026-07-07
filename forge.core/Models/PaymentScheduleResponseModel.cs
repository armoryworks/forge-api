namespace Forge.Core.Models;

/// <summary>
/// Read model for a quote/sales-order pre-payment schedule. The same row is
/// visible from both documents — it is defined on the quote and re-linked
/// (SalesOrderId set) at conversion, never cloned.
/// </summary>
public record PaymentScheduleResponseModel(
    int Id,
    int? QuoteId,
    int? SalesOrderId,
    string Status,
    List<PaymentMilestoneResponseModel> Milestones,
    PaymentScheduleTotalsModel Totals);
