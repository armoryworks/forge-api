
namespace Forge.Core.Models;

public record LeaveRequestResponseModel(
    int Id,
    int UserId,
    string UserName,
    int PolicyId,
    string PolicyName,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal Hours,
    LeaveRequestStatus Status,
    int? ApprovedById,
    string? ApprovedByName,
    DateTimeOffset? DecidedAt,
    string? Reason,
    string? DenialReason,
    DateTimeOffset CreatedAt);
