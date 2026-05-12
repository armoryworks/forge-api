namespace Forge.Core.Models;

public record CompanyLocationRequestModel(
    string Name,
    string Line1,
    string? Line2,
    string City,
    string State,
    string PostalCode,
    string Country,
    string? Phone,
    bool IsActive);
