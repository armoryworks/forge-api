namespace Forge.Core.Models;

public record CreateReviewCycleRequestModel(
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Description);
