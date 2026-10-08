namespace Forge.Core.Models;

public record CreateVendorContactRequestModel(
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? Mobile,
    string? Fax,
    string? Role,
    bool IsPrimary,
    string? Notes,
    bool IsActive = true);
