using System.Text.Json;

using FluentAssertions;

using Forge.Api.Features.Training;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Training;

public class TrainingProgressIntegrityTests
{
    private const int UserId = 21;
    private const int ArticleId = 1;
    private const int QuizId = 2;
    private const int PooledQuizId = 3;

    private readonly AppDbContext _db = TestDbContextFactory.Create();

    public TrainingProgressIntegrityTests()
    {
        _db.TrainingModules.AddRange(
            new TrainingModule { Id = ArticleId, Title = "Article", Slug = "article", ContentType = TrainingContentType.Article, IsPublished = true },
            new TrainingModule { Id = QuizId, Title = "Quiz", Slug = "quiz", ContentType = TrainingContentType.Quiz, IsPublished = true, ContentJson = QuizJson(null) },
            new TrainingModule { Id = PooledQuizId, Title = "Pooled", Slug = "pooled", ContentType = TrainingContentType.Quiz, IsPublished = true, ContentJson = QuizJson(1) });
        _db.SaveChanges();
    }

    private static string QuizJson(int? questionsPerQuiz) => JsonSerializer.Serialize(new
    {
        passingScore = 100,
        questionsPerQuiz,
        questions = new[]
        {
            new { id = "q1", text = "One", explanation = "Because one", options = new[] { new { id = "a", text = "A", isCorrect = true }, new { id = "b", text = "B", isCorrect = false } } },
            new { id = "q2", text = "Two", explanation = "Because two", options = new[] { new { id = "c", text = "C", isCorrect = true }, new { id = "d", text = "D", isCorrect = false } } },
        },
    });

    [Fact]
    public async Task Complete_UnknownModule_IsNotFound()
    {
        var act = () => new CompleteModuleHandler(_db).Handle(new CompleteModuleCommand(UserId, 999), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Complete_Quiz_IsRefused()
    {
        var act = () => new CompleteModuleHandler(_db).Handle(new CompleteModuleCommand(UserId, QuizId), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _db.TrainingProgress.Should().BeEmpty();
    }

    [Fact]
    public async Task Complete_Article_IsRecorded()
    {
        await new CompleteModuleHandler(_db).Handle(new CompleteModuleCommand(UserId, ArticleId), CancellationToken.None);

        _db.TrainingProgress.Single().Status.Should().Be(TrainingProgressStatus.Completed);
    }

    [Fact]
    public async Task StartAndHeartbeat_UnknownModule_AreNotFound()
    {
        var start = () => new RecordModuleStartHandler(_db).Handle(new RecordModuleStartCommand(UserId, 999), CancellationToken.None);
        var beat = () => new RecordProgressHeartbeatHandler(_db).Handle(new RecordProgressHeartbeatCommand(UserId, 999, 30), CancellationToken.None);

        await start.Should().ThrowAsync<KeyNotFoundException>();
        await beat.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Heartbeat_IsClampedToOneInterval()
    {
        var handler = new RecordProgressHeartbeatHandler(_db);
        await handler.Handle(new RecordProgressHeartbeatCommand(UserId, ArticleId, 1_000_000), CancellationToken.None);
        await handler.Handle(new RecordProgressHeartbeatCommand(UserId, ArticleId, 30), CancellationToken.None);

        _db.TrainingProgress.Single().TimeSpentSeconds.Should().Be(RecordProgressHeartbeatHandler.MaxSecondsPerHeartbeat + 30);
    }

    [Fact]
    public async Task FailedSubmit_HidesAnswers_AndReturnsPassingScore()
    {
        var result = await Submit(QuizId, isAdmin: false, ("q1", "b"), ("q2", "c"));

        result.Passed.Should().BeFalse();
        result.PassingScore.Should().Be(100);
        result.Questions.Should().OnlyContain(q => q.CorrectOptionId == null && q.Explanation == null);
        result.Questions.Single(q => q.QuestionId == "q2").IsCorrect.Should().BeTrue();
    }

    [Fact]
    public async Task PassedSubmit_RevealsAnswers()
    {
        var result = await Submit(QuizId, isAdmin: false, ("q1", "a"), ("q2", "c"));

        result.Passed.Should().BeTrue();
        result.Questions.Should().OnlyContain(q => q.CorrectOptionId != null && q.Explanation != null);
    }

    [Fact]
    public async Task PooledQuiz_WithNoSession_IsRefusedForAUser()
    {
        var act = () => Submit(PooledQuizId, isAdmin: false, ("q1", "a"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task PooledQuiz_WithNoSession_IsScoredForAnAdmin()
    {
        var result = await Submit(PooledQuizId, isAdmin: true, ("q1", "a"), ("q2", "c"));

        result.Score.Should().Be(100);
    }

    [Fact]
    public async Task NonPooledQuiz_WithNoSession_IsScoredForAUser()
    {
        _db.TrainingProgress.Should().BeEmpty();

        var result = await Submit(QuizId, isAdmin: false, ("q1", "a"), ("q2", "d"));

        result.Score.Should().Be(50);
        result.Questions.Should().HaveCount(2);
    }

    [Fact]
    public async Task DuplicateAnswers_ForOneQuestion_UseTheLastAnswer()
    {
        var result = await Submit(QuizId, isAdmin: false, ("q1", "b"), ("q2", "c"), ("q1", "a"));

        result.Score.Should().Be(100);
        result.Passed.Should().BeTrue();
    }

    [Fact]
    public async Task PooledQuiz_ScoresOnlyTheSessionQuestions()
    {
        _db.TrainingProgress.Add(new TrainingProgress
        {
            UserId = UserId, ModuleId = PooledQuizId, Status = TrainingProgressStatus.NotStarted,
            QuizSessionJson = JsonSerializer.Serialize(new[] { "q2" }),
        });
        await _db.SaveChangesAsync();

        var result = await Submit(PooledQuizId, isAdmin: false, ("q1", "b"), ("q2", "c"));

        result.Score.Should().Be(100);
        result.Questions.Select(q => q.QuestionId).Should().Equal("q2");
    }

    private Task<QuizSubmissionResponseModel> Submit(int moduleId, bool isAdmin, params (string Q, string O)[] answers) =>
        new SubmitQuizHandler(_db).Handle(
            new SubmitQuizCommand(UserId, moduleId, answers.Select(a => new QuizAnswerModel(a.Q, a.O)).ToList(), isAdmin),
            CancellationToken.None);
}
