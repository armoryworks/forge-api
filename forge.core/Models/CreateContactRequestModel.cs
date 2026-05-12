namespace Forge.Core.Models;

public record CreateContactRequestModel(
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? Role,
    bool IsPrimary);
