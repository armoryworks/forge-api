
namespace Forge.Core.Models;

public record AdminUpdateEmployeeProfileRequestModel(
    // Employment
    DateTimeOffset? StartDate,
    string? Department,
    string? JobTitle,
    string? EmployeeNumber,
    PayType? PayType,
    decimal? HourlyRate,
    decimal? SalaryAmount,

    // Tax/Compliance completion dates
    DateTimeOffset? W4CompletedAt,
    DateTimeOffset? StateWithholdingCompletedAt,
    DateTimeOffset? I9CompletedAt,
    DateTimeOffset? I9ExpirationDate,
    DateTimeOffset? DirectDepositCompletedAt,
    DateTimeOffset? WorkersCompAcknowledgedAt,
    DateTimeOffset? HandbookAcknowledgedAt);
