using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>A refund of a credit memo's open amount or of a payment's unapplied cash. <c>SettlementFxRate</c> defaults to the source document's rate.</summary>
public record CustomerRefundRequestModel(
    decimal Amount,
    PaymentMethod Method,
    DateTimeOffset RefundDate,
    string? Reference,
    decimal? SettlementFxRate = null,
    string? Notes = null);
