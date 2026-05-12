namespace Forge.Core.Models;

public record AccountingCustomer(
    string ExternalId,
    string Name,
    string? Email,
    string? Phone,
    string? CompanyName,
    decimal Balance);
