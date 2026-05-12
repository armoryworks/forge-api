namespace Forge.Core.Models;

public record CustomerListItemModel(
    int Id,
    string Name,
    string? CompanyName,
    string? Email,
    string? Phone,
    bool IsActive,
    int ContactCount,
    int JobCount,
    DateTimeOffset CreatedAt);
