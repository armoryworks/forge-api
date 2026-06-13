namespace Forge.Core.Models;

/// <summary>
/// Outcome of one bank submission attempt: on success <paramref name="SubmissionRef"/> carries the
/// bank-issued reference; on failure <paramref name="Error"/> carries the channel error message.
/// </summary>
public record BankSubmissionResult(
    bool Success,
    string? SubmissionRef,
    string? Error);
