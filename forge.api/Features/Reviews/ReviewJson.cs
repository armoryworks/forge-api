using System.Text.Json;

using Forge.Core.Models;

namespace Forge.Api.Features.Reviews;

public static class ReviewJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string? Write<T>(T? value) where T : class
        => value is null ? null : JsonSerializer.Serialize(value, Options);

    public static IReadOnlyList<string> Competencies(string? json)
        => string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<string>>(json, Options) ?? [];

    public static IReadOnlyList<int> UserIds(string? json)
        => string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<int>>(json, Options) ?? [];

    public static List<string> GoalLines(string goals)
        => goals.Split('\n').Select(g => g.Trim()).Where(g => g.Length > 0).ToList();

    public static void EnsureRatingsInRange(IReadOnlyList<ReviewCompetencyRatingModel>? ratings, int scaleMax)
    {
        foreach (var rating in ratings ?? [])
        {
            if (string.IsNullOrWhiteSpace(rating.Competency))
                throw new InvalidOperationException("Every rating needs a competency.");
            if (rating.Rating is decimal r && (r < 1 || r > scaleMax))
                throw new InvalidOperationException($"Ratings run from 1 to {scaleMax}.");
        }
    }
}
