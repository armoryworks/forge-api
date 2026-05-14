using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Training;

public record SubmitQuizCommand(int UserId, int ModuleId, List<QuizAnswerModel> Answers)
    : IRequest<QuizSubmissionResponseModel>;

public class SubmitQuizHandler(AppDbContext db) : IRequestHandler<SubmitQuizCommand, QuizSubmissionResponseModel>
{
    public async Task<QuizSubmissionResponseModel> Handle(SubmitQuizCommand request, CancellationToken ct)
    {
        var module = await db.TrainingModules.AsNoTracking().FirstOrDefaultAsync(m => m.Id == request.ModuleId, ct)
            ?? throw new KeyNotFoundException($"Training module {request.ModuleId} not found.");

        if (module.ContentType != TrainingContentType.Quiz)
            throw new InvalidOperationException("Module is not a quiz.");

        var progress = await db.TrainingProgress
            .FirstOrDefaultAsync(p => p.UserId == request.UserId && p.ModuleId == request.ModuleId, ct);

        var content = ParseQuizContent(module.ContentJson);

        // If the quiz has randomized sessions, score only the questions that were presented
        var sessionIds = DeserializeSessionIds(progress?.QuizSessionJson);
        var questionsToScore = sessionIds.Count > 0
            ? content.Questions.Where(q => sessionIds.Contains(q.Id)).ToList()
            : content.Questions;

        var answerMap = request.Answers.ToDictionary(a => a.QuestionId, a => a.OptionId);

        var correctCount = 0;
        var scoredQuestions = new List<QuizScoredQuestionModel>();

        foreach (var question in questionsToScore)
        {
            var correctOption = question.Options.FirstOrDefault(o => o.IsCorrect);
            if (correctOption is null) continue;

            answerMap.TryGetValue(question.Id, out var givenOptionId);
            var isCorrect = givenOptionId == correctOption.Id;
            if (isCorrect) correctCount++;

            scoredQuestions.Add(new QuizScoredQuestionModel(
                question.Id,
                isCorrect,
                correctOption.Id,
                question.Explanation
            ));
        }

        var totalQuestions = questionsToScore.Count(q => q.Options.Any(o => o.IsCorrect));
        var score = totalQuestions > 0 ? (int)Math.Round((double)correctCount / totalQuestions * 100) : 0;
        var passed = score >= content.PassingScore;

        var answersJson = JsonSerializer.Serialize(request.Answers);

        if (progress is null)
        {
            progress = new TrainingProgress
            {
                UserId = request.UserId,
                ModuleId = request.ModuleId,
                Status = passed ? TrainingProgressStatus.Completed : TrainingProgressStatus.InProgress,
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = passed ? DateTimeOffset.UtcNow : null,
                QuizScore = score,
                QuizAttempts = 1,
                QuizAnswersJson = answersJson,
                // Clear session on failure so next load generates a fresh random set
                QuizSessionJson = passed ? progress?.QuizSessionJson : null,
            };
            db.TrainingProgress.Add(progress);
        }
        else
        {
            progress.QuizScore = score;
            progress.QuizAttempts = (progress.QuizAttempts ?? 0) + 1;
            progress.QuizAnswersJson = answersJson;

            if (passed && progress.Status != TrainingProgressStatus.Completed)
            {
                progress.Status = TrainingProgressStatus.Completed;
                progress.CompletedAt ??= DateTimeOffset.UtcNow;
                // Preserve session on pass (used for post-pass review if needed)
            }
            else if (!passed)
            {
                // Clear session — next GET will generate a new random question set
                progress.QuizSessionJson = null;
            }
        }

        await db.SaveChangesAsync(ct);

        if (passed)
            await CheckAndCompleteEnrollmentsAsync(request.UserId, ct);

        return new QuizSubmissionResponseModel(score, passed, scoredQuestions.ToArray());
    }

    private async Task CheckAndCompleteEnrollmentsAsync(int userId, CancellationToken ct)
    {
        var enrollments = await db.TrainingPathEnrollments
            .Include(e => e.Path)
                .ThenInclude(p => p.PathModules)
            .Where(e => e.UserId == userId && e.CompletedAt == null)
            .ToListAsync(ct);

        if (enrollments.Count == 0) return;

        var allRequiredModuleIds = enrollments
            .SelectMany(e => e.Path.PathModules.Where(pm => pm.IsRequired).Select(pm => pm.ModuleId))
            .Distinct()
            .ToList();

        var completedModuleIds = await db.TrainingProgress
            .Where(p => p.UserId == userId
                        && allRequiredModuleIds.Contains(p.ModuleId)
                        && p.Status == TrainingProgressStatus.Completed)
            .Select(p => p.ModuleId)
            .ToListAsync(ct);

        var completedSet = new HashSet<int>(completedModuleIds);
        var now = DateTimeOffset.UtcNow;
        var anyChanged = false;

        foreach (var enrollment in enrollments)
        {
            var requiredIds = enrollment.Path.PathModules
                .Where(pm => pm.IsRequired)
                .Select(pm => pm.ModuleId)
                .ToList();

            if (requiredIds.Count > 0 && requiredIds.All(id => completedSet.Contains(id)))
            {
                enrollment.CompletedAt = now;
                anyChanged = true;
            }
        }

        if (anyChanged)
            await db.SaveChangesAsync(ct);
    }

    private static List<string> DeserializeSessionIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch { return []; }
    }

    private static QuizContent ParseQuizContent(string contentJson)
    {
        try
        {
            return JsonSerializer.Deserialize<QuizContent>(contentJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new QuizContent([], 70);
        }
        catch
        {
            return new QuizContent([], 70);
        }
    }

    private record QuizContent(List<QuizQuestion> Questions, int PassingScore);
    private record QuizQuestion(string Id, string Text, List<QuizOption> Options, string? Explanation);
    private record QuizOption(string Id, string Text, bool IsCorrect);
}
