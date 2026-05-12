namespace Forge.Core.Models;

public record ContactResponseModel(
    int Id,
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? Role,
    bool IsPrimary);
