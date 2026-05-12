namespace Forge.Core.Models;

public record AccountingPayStubDeduction(
    string Category,
    string Description,
    decimal Amount);
