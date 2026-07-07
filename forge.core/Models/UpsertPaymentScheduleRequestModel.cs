namespace Forge.Core.Models;

/// <summary>
/// PUT body for the bulk-replace payment-schedule upsert on a quote.
/// </summary>
public record UpsertPaymentScheduleRequestModel(List<PaymentMilestoneRequestModel> Milestones);
