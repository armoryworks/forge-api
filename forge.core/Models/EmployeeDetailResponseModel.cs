namespace Forge.Core.Models;

public record EmployeeDetailResponseModel(
    int Id,
    // Phase 3 / WU-19 / F9: nullable when Employee has no User account.
    // For a linked Employee, equals the User id; for a User-less Employee
    // returned via the EmployeeProfile.Id path, this is null.
    int? UserId,
    string FirstName,
    string LastName,
    string? Initials,
    string? AvatarColor,
    string Email,
    string? Phone,
    string Role,
    string? TeamName,
    int? TeamId,
    bool IsActive,
    string? JobTitle,
    string? Department,
    DateTimeOffset? StartDate,
    DateTimeOffset CreatedAt,
    int? WorkLocationId,
    string? WorkLocationName,
    bool PinConfigured,
    bool HasRfidIdentifier,
    bool HasBarcodeIdentifier,
    string? PersonalEmail,
    string? Street1,
    string? Street2,
    string? City,
    string? State,
    string? ZipCode,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    string? EmergencyContactRelationship,
    int ComplianceCompletedItems,
    int ComplianceTotalItems);
