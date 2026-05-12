namespace Forge.Core.Models;

public record AccountingTimeActivity(
    string EmployeeExternalId,
    string? CustomerExternalId,
    decimal Hours,
    DateTimeOffset Date,
    string? Description,
    string? ServiceItemExternalId);
