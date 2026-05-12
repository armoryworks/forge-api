namespace Forge.Core.Models;

public record UpdateContactRequestModel(
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? Role,
    bool? IsPrimary);
