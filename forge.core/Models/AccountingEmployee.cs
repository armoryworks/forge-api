namespace Forge.Core.Models;

public record AccountingEmployee(
    string ExternalId,
    string DisplayName,
    string? Email,
    string? Phone,
    bool Active);
