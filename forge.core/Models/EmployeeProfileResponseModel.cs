
namespace Forge.Core.Models;

public record EmployeeProfileResponseModel(
    int Id,
    int? UserId,

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
    string? EmergencyContactRelationship,

    // Employment
    DateTimeOffset? StartDate,
    string? Department,
    string? JobTitle,
    string? EmployeeNumber,
    PayType? PayType,
    decimal? HourlyRate,
    decimal? SalaryAmount,

    // Tax/Compliance
    DateTimeOffset? W4CompletedAt,
    DateTimeOffset? StateWithholdingCompletedAt,
    DateTimeOffset? I9CompletedAt,
    DateTimeOffset? I9ExpirationDate,
    DateTimeOffset? DirectDepositCompletedAt,
    DateTimeOffset? WorkersCompAcknowledgedAt,
    DateTimeOffset? HandbookAcknowledgedAt);
