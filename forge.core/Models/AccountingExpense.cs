namespace Forge.Core.Models;

public record AccountingExpense(
    string? VendorExternalId,
    string? CustomerExternalId,
    decimal Amount,
    DateTimeOffset Date,
    string? Description,
    string? Category,
    string? RefNumber);
