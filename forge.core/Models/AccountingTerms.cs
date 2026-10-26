namespace Forge.Core.Models;

/// <summary>Payment terms sent with a sales document; the provider finds or creates its own terms record by name.</summary>
public record AccountingTerms(string Name, int DueDays, decimal DiscountPercent, int DiscountDays);
