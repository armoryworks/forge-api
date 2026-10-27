namespace Forge.Core.Models;

public record SubmitManagerReviewRequestModel(
    IReadOnlyList<ReviewCompetencyRatingModel>? Ratings,
    decimal OverallRating,
    string? Goals,
    string? StrengthsComments,
    string? ImprovementComments);
