namespace Forge.Core.Models;

public record MfaPolicyRequestModel
{
    public IReadOnlyList<string> RequiredRoles { get; init; } = [];
    public int? EnrollmentGraceDays { get; init; }
}

public record MfaPolicyResponseModel
{
    public IReadOnlyList<string> RequiredRoles { get; init; } = [];
    public int EnrollmentGraceDays { get; init; }
}
