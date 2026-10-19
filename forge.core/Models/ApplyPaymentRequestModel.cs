namespace Forge.Core.Models;

/// <summary>Payload for applying a payment's unapplied (on-account) cash to open invoices.</summary>
public record ApplyPaymentRequestModel(List<CreatePaymentApplicationModel> Applications);
