namespace Forge.Core.Models;

public record AccountingPayment(
    string ExternalId,
    decimal Amount,
    DateTimeOffset Date,
    string? Method);
