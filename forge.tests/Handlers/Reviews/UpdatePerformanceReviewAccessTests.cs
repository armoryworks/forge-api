using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Reviews;
using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Reviews;

public class UpdatePerformanceReviewAccessTests
{
    private readonly TimerTestHarness _h = new();

    private async Task<(int Ben, int Lena, int Rosa, PerformanceReview Review)> SeedAsync(bool lenaReviewsHerself = false)
    {
        var ben = (await _h.AddUserAsync()).Id;
        var lena = (await _h.AddUserAsync()).Id;
        var rosa = (await _h.AddUserAsync()).Id;
        var cycle = new ReviewCycle { Name = "2026 H2", StartDate = new DateOnly(2026, 7, 1), EndDate = new DateOnly(2026, 12, 31) };
        _h.Db.ReviewCycles.Add(cycle);
        await _h.Db.SaveChangesAsync();
        var review = new PerformanceReview { CycleId = cycle.Id, EmployeeId = lenaReviewsHerself ? lena : ben, ReviewerId = lena };
        _h.Db.PerformanceReviews.Add(review);
        await _h.Db.SaveChangesAsync();
        return (ben, lena, rosa, review);
    }

    private Task Patch(int reviewId, int caller, string[] roles, ReviewStatus? status = null, decimal? rating = null,
        string? strengths = null, string? selfAssessment = null)
        => new UpdatePerformanceReviewHandler(_h.Db, TestCaller.As(caller, roles)).Handle(
            new UpdatePerformanceReviewCommand(reviewId, status, rating, null, null, strengths, null, selfAssessment),
            CancellationToken.None);

    private Task<PerformanceReview> ReloadAsync(int id)
        => _h.Db.PerformanceReviews.AsNoTracking().SingleAsync(r => r.Id == id);

    [Fact]
    public async Task The_reviewer_rates_and_the_employee_writes_the_self_assessment()
    {
        var (ben, lena, _, review) = await SeedAsync();

        await Patch(review.Id, lena, ["Manager"], rating: 4m, strengths: "Owns the 5-axis cell");
        await Patch(review.Id, ben, ["ProductionWorker"], selfAssessment: "Cut OP20 setup to 32 min.");

        var saved = await ReloadAsync(review.Id);
        saved.OverallRating.Should().Be(4m);
        saved.EmployeeSelfAssessment.Should().Be("Cut OP20 setup to 32 min.");
    }

    [Fact]
    public async Task Another_worker_cannot_touch_the_review()
    {
        var (_, _, rosa, review) = await SeedAsync();

        await FluentActions.Awaiting(() => Patch(review.Id, rosa, ["ProductionWorker"], rating: 1m, strengths: "rewritten"))
            .Should().ThrowAsync<ForbiddenException>();
        (await ReloadAsync(review.Id)).OverallRating.Should().BeNull();
    }

    [Fact]
    public async Task The_employee_cannot_rate_themselves_and_the_reviewer_cannot_write_the_self_assessment()
    {
        var (ben, lena, _, review) = await SeedAsync();

        await FluentActions.Awaiting(() => Patch(review.Id, ben, [], rating: 5m)).Should().ThrowAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => Patch(review.Id, lena, ["Manager"], selfAssessment: "x")).Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task A_manager_who_is_the_subject_cannot_rate_or_complete_her_own_review()
    {
        var (_, lena, _, review) = await SeedAsync(lenaReviewsHerself: true);

        await FluentActions.Awaiting(() => Patch(review.Id, lena, ["Manager"], ReviewStatus.Completed, 5m))
            .Should().ThrowAsync<ForbiddenException>();
        await Patch(review.Id, lena, ["Manager"], selfAssessment: "My year");

        var saved = await ReloadAsync(review.Id);
        saved.OverallRating.Should().BeNull();
        saved.Status.Should().Be(ReviewStatus.NotStarted);
    }

    [Fact]
    public async Task A_manager_who_is_not_the_reviewer_cannot_rate_but_an_admin_can()
    {
        var (_, _, rosa, review) = await SeedAsync();

        await FluentActions.Awaiting(() => Patch(review.Id, rosa, ["Manager"], rating: 2m)).Should().ThrowAsync<ForbiddenException>();
        await Patch(review.Id, rosa, ["Admin"], rating: 3m);

        (await ReloadAsync(review.Id)).OverallRating.Should().Be(3m);
    }
}
