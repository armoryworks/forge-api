namespace Forge.Core.Models;

public record UpdateEmployeeProfileRequestModel(
    // Personal
    DateTimeOffset? DateOfBirth,
    string? Gender,

    // Address
    string? Street1,
    string? Street2,
    string? City,
    string? State,
    string? ZipCode,
    string? Country,

    // Contact
    string? PhoneNumber,
    string? PersonalEmail,

    // Emergency
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    string? EmergencyContactRelationship);
