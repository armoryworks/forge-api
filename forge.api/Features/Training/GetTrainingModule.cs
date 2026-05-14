using System.Text.Json;
using System.Text.Json.Nodes;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Training;

public record GetTrainingModuleQuery(int Id, int UserId, bool IsAdmin) : IRequest<TrainingModuleDetailResponseModel>;

public class GetTrainingModuleHandler(AppDbContext db)
    : IRequestHandler<GetTrainingModuleQuery, TrainingModuleDetailResponseModel>
{
    private static readonly Random Rng = Random.Shared;

    public async Task<TrainingModuleDetailResponseModel> Handle(GetTrainingModuleQuery request, CancellationToken ct)
    {
        var query = db.TrainingModules.AsNoTracking().Where(m => m.Id == request.Id);

        if (!request.IsAdmin)
            query = query.Where(m => m.IsPublished);

        var module = await query.FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Training module {request.Id} not found.");

        var progress = await db.TrainingProgress
            .FirstOrDefaultAsync(p => p.UserId == request.UserId && p.ModuleId == request.Id, ct);

        var contentJson = module.ContentJson;

        if (!request.IsAdmin && module.ContentType == TrainingContentType.Quiz)
            contentJson = await PrepareQuizForUserAsync(contentJson, progress, request.UserId, request.Id, ct);

        return new TrainingModuleDetailResponseModel(
            module.Id,
            module.Title,
            module.Slug,
            module.Summary,
            module.ContentType,
            module.CoverImageUrl,
            module.EstimatedMinutes,
            ParseJsonStringArray(module.Tags),
            module.IsPublished,
            module.IsOnboardingRequired,
            module.SortOrder,
            progress?.Status,
            progress?.QuizScore,
            progress?.CompletedAt,
            contentJson,
            ParseJsonStringArray(module.AppRoutes),
            module.CreatedAt,
            module.UpdatedAt
        );
    }

    /// <summary>
    /// For non-admin users:
    /// 1. Selects a random subset of questions if questionsPerQuiz &lt; total in pool.
    /// 2. Shuffles option order per question if shuffleOptions is true.
    /// 3. Persists the selected question IDs to TrainingProgress.QuizSessionJson so the
    ///    same questions are returned on subsequent loads (stable session), and so SubmitQuiz
    ///    can score only the presented questions.
    /// 4. Strips isCorrect and passingScore before returning.
    /// </summary>
    private async Task<string> PrepareQuizForUserAsync(
        string contentJson,
        TrainingProgress? progress,
        int userId,
        int moduleId,
        CancellationToken ct)
    {
        try
        {
            var root = JsonNode.Parse(contentJson)?.AsObject();
            if (root is null) return contentJson;

            var allQuestions = root["questions"]?.AsArray();
            if (allQuestions is null || allQuestions.Count == 0)
                return StripMetadata(root);

            var questionsPerQuiz = root["questionsPerQuiz"]?.GetValue<int?>();
            var shuffleOptions = root["shuffleOptions"]?.GetValue<bool>() ?? false;
            var totalInPool = allQuestions.Count;

            // Determine which question IDs to present this session
            List<string> sessionIds;

            if (questionsPerQuiz.HasValue && questionsPerQuiz.Value < totalInPool)
            {
                // Re-use existing session if the user hasn't submitted yet (stable mid-quiz experience)
                var existingSession = DeserializeSessionIds(progress?.QuizSessionJson);

                if (existingSession.Count > 0 && progress?.Status != TrainingProgressStatus.Completed)
                {
                    sessionIds = existingSession;
                }
                else
                {
                    // Generate a new random selection
                    var allIds = allQuestions
                        .Select(q => q?["id"]?.GetValue<string>())
                        .Where(id => id is not null)
                        .Cast<string>()
                        .ToList();

                    sessionIds = allIds
                        .OrderBy(_ => Rng.Next())
                        .Take(questionsPerQuiz.Value)
                        .ToList();

                    await PersistSessionAsync(userId, moduleId, sessionIds, progress, ct);
                }
            }
            else
            {
                // Use all questions — still shuffle their order if requested
                sessionIds = allQuestions
                    .Select(q => q?["id"]?.GetValue<string>())
                    .Where(id => id is not null)
                    .Cast<string>()
                    .ToList();
            }

            // Build the selected question set in session order
            var questionMap = allQuestions
                .Where(q => q is not null)
                .ToDictionary(q => q!["id"]!.GetValue<string>(), q => q!.DeepClone());

            var selectedQuestions = new JsonArray();
            foreach (var id in sessionIds)
            {
                if (!questionMap.TryGetValue(id, out var q)) continue;

                if (shuffleOptions)
                    ShuffleOptions(q);

                // Strip isCorrect from every option
                var options = q["options"]?.AsArray();
                if (options is not null)
                    foreach (var opt in options)
                        opt?.AsObject().Remove("isCorrect");

                selectedQuestions.Add(q);
            }

            root["questions"] = selectedQuestions;

            // Add pool size info so the frontend can display "X of Y questions"
            root["poolSize"] = totalInPool;

            // Strip server-only fields
            root.Remove("passingScore");

            return root.ToJsonString();
        }
        catch
        {
            return contentJson;
        }
    }

    private static void ShuffleOptions(JsonNode question)
    {
        var options = question["options"]?.AsArray();
        if (options is null || options.Count <= 1) return;

        var clones = options.Select(o => o!.DeepClone()).ToList();
        // Fisher-Yates
        for (var i = clones.Count - 1; i > 0; i--)
        {
            var j = Rng.Next(i + 1);
            (clones[i], clones[j]) = (clones[j], clones[i]);
        }

        options.Clear();
        foreach (var item in clones)
            options.Add(item);
    }

    private async Task PersistSessionAsync(
        int userId,
        int moduleId,
        List<string> sessionIds,
        TrainingProgress? existing,
        CancellationToken ct)
    {
        var sessionJson = JsonSerializer.Serialize(sessionIds);

        if (existing is null)
        {
            var progress = new TrainingProgress
            {
                UserId = userId,
                ModuleId = moduleId,
                Status = TrainingProgressStatus.NotStarted,
                QuizSessionJson = sessionJson,
            };
            db.TrainingProgress.Add(progress);
        }
        else
        {
            existing.QuizSessionJson = sessionJson;
        }

        await db.SaveChangesAsync(ct);
    }

    private static List<string> DeserializeSessionIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch { return []; }
    }

    private static string StripMetadata(JsonObject root)
    {
        root.Remove("passingScore");
        return root.ToJsonString();
    }

    private static string[] ParseJsonStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch { return []; }
    }
}
