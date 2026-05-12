namespace Forge.Core.Models;

public record AccountingTaxDocument(
    string ExternalId,
    string DocumentType,
    int TaxYear,
    string? EmployerName);
