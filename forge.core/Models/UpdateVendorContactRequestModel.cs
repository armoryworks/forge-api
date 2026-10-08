namespace Forge.Core.Models;

public record UpdateVendorContactRequestModel(
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? Mobile,
    string? Fax,
    string? Role,
    bool? IsPrimary,
    bool? IsActive,
    string? Notes);
