namespace Forge.Core.Models;

public record SubmitSelfReviewRequestModel(
    IReadOnlyList<ReviewCompetencyRatingModel>? Ratings,
    string? SelfAssessment);
