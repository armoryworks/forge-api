using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Reviews;
using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Reviews;

public class ReviewLifecycleTests
{
    private readonly TimerTestHarness _h = new();

    private sealed record Crew(int Admin, int Manager, int Worker, int OtherWorker, int Outsider);

    private async Task<Crew> SeedCrewAsync()
    {
        var admin = await _h.AddUserAsync();
        var manager = await _h.AddUserAsync();
        var worker = await _h.AddUserAsync();
        var other = await _h.AddUserAsync();
        var outsider = await _h.AddUserAsync();
        manager.TeamId = 1;
        worker.TeamId = 1;
        other.TeamId = 1;
        outsider.TeamId = 2;
        admin.IsNonEmployee = true;
        var managerRole = new IdentityRole<int> { Name = "Manager", NormalizedName = "MANAGER" };
        _h.Db.Roles.Add(managerRole);
        await _h.Db.SaveChangesAsync();
        _h.Db.UserRoles.Add(new IdentityUserRole<int> { UserId = manager.Id, RoleId = managerRole.Id });
        _h.Db.EmployeeProfiles.Add(new EmployeeProfile { UserId = worker.Id, Department = "Machining" });
        await _h.Db.SaveChangesAsync();
        return new Crew(admin.Id, manager.Id, worker.Id, other.Id, outsider.Id);
    }

    private async Task<int> LaunchAsync(Crew crew, string scope = ReviewScopeTypes.All, string? department = null)
    {
        var cycle = await new CreateReviewCycleHandler(_h.Db).Handle(new CreateReviewCycleCommand(
            new CreateReviewCycleRequestModel("2026 annual", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), null,
                5, ["Quality", "Safety"], scope, department)), CancellationToken.None);
        await new LaunchReviewCycleHandler(_h.Db, _h.Mediator.Object, _h.Clock.Object)
            .Handle(new LaunchReviewCycleCommand(cycle.Id, crew.Admin), CancellationToken.None);
        return cycle.Id;
    }

    private Task<PerformanceReview> ReviewOfAsync(int employeeId)
        => _h.Db.PerformanceReviews.Include(r => r.Cycle).SingleAsync(r => r.EmployeeId == employeeId);

    [Fact]
    public async Task Launch_creates_one_review_per_employee_with_the_team_manager_as_reviewer()
    {
        var crew = await SeedCrewAsync();

        var cycleId = await LaunchAsync(crew);

        var reviews = await _h.Db.PerformanceReviews.Where(r => r.CycleId == cycleId).ToListAsync();
        reviews.Select(r => r.EmployeeId).Should().BeEquivalentTo([crew.Manager, crew.Worker, crew.OtherWorker, crew.Outsider]);
        reviews.Single(r => r.EmployeeId == crew.Worker).ReviewerId.Should().Be(crew.Manager);
        reviews.Single(r => r.EmployeeId == crew.Manager).ReviewerId.Should().Be(crew.Admin);
        reviews.Single(r => r.EmployeeId == crew.Outsider).ReviewerId.Should().Be(crew.Admin);
        (await _h.Db.ReviewCycles.SingleAsync(c => c.Id == cycleId)).Status.Should().Be(ReviewCycleStatus.Active);
    }

    [Fact]
    public async Task A_department_scope_reviews_only_that_department()
    {
        var crew = await SeedCrewAsync();

        var cycleId = await LaunchAsync(crew, ReviewScopeTypes.Department, "Machining");

        (await _h.Db.PerformanceReviews.Where(r => r.CycleId == cycleId).Select(r => r.EmployeeId).ToListAsync())
            .Should().Equal(crew.Worker);
    }

    [Fact]
    public async Task The_review_walks_self_manager_acknowledge_close()
    {
        var crew = await SeedCrewAsync();
        await LaunchAsync(crew);
        var review = await ReviewOfAsync(crew.Worker);

        await FluentActions.Awaiting(() => new SubmitManagerReviewHandler(_h.Db, TestCaller.As(crew.Manager, "Manager"), _h.Clock.Object, _h.Mediator.Object)
                .Handle(new SubmitManagerReviewCommand(review.Id, new SubmitManagerReviewRequestModel(null, 4, null, null, null)), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();

        await new SubmitSelfReviewHandler(_h.Db, TestCaller.As(crew.Worker), _h.Clock.Object).Handle(
            new SubmitSelfReviewCommand(review.Id, new SubmitSelfReviewRequestModel(
                [new ReviewCompetencyRatingModel("Quality", 4, "Zero escapes")], "Good year")), CancellationToken.None);
        review.Status.Should().Be(ReviewStatus.SelfSubmitted);

        var managerView = await new SubmitManagerReviewHandler(_h.Db, TestCaller.As(crew.Manager, "Manager"), _h.Clock.Object, _h.Mediator.Object)
            .Handle(new SubmitManagerReviewCommand(review.Id, new SubmitManagerReviewRequestModel(
                [new ReviewCompetencyRatingModel("Quality", 5, null)], 4.5m, "Lead the cell\nTrain two operators", "Reliable", null)), CancellationToken.None);
        managerView.Status.Should().Be(ReviewStatus.ManagerSubmitted);
        managerView.EmployeeSelfAssessment.Should().Be("Good year");

        await new AcknowledgeReviewHandler(_h.Db, TestCaller.As(crew.Worker), _h.Clock.Object).Handle(
            new AcknowledgeReviewCommand(review.Id, new AcknowledgeReviewRequestModel("Thanks")), CancellationToken.None);
        review.Status.Should().Be(ReviewStatus.Acknowledged);
        review.AcknowledgedAt.Should().Be(_h.Now);

        await new ClosePerformanceReviewHandler(_h.Db, TestCaller.As(crew.Manager, "Manager"), _h.Clock.Object).Handle(
            new ClosePerformanceReviewCommand(review.Id), CancellationToken.None);
        review.Status.Should().Be(ReviewStatus.Closed);
        _h.Db.ActivityLogs.Where(a => a.EntityType == "PerformanceReview" && a.EntityId == review.Id).Select(a => a.Action)
            .Should().ContainInOrder("created", "self-submitted", "manager-submitted", "acknowledged", "closed");
    }

    [Fact]
    public async Task Ratings_outside_the_scale_are_refused()
    {
        var crew = await SeedCrewAsync();
        await LaunchAsync(crew);
        var review = await ReviewOfAsync(crew.Worker);

        await FluentActions.Awaiting(() => new SubmitSelfReviewHandler(_h.Db, TestCaller.As(crew.Worker), _h.Clock.Object).Handle(
                new SubmitSelfReviewCommand(review.Id, new SubmitSelfReviewRequestModel([new ReviewCompetencyRatingModel("Quality", 9, null)], null)),
                CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task An_employee_cannot_read_or_act_on_a_colleagues_review()
    {
        var crew = await SeedCrewAsync();
        await LaunchAsync(crew);
        var review = await ReviewOfAsync(crew.Worker);

        await FluentActions.Awaiting(() => new GetPerformanceReviewHandler(_h.Db, TestCaller.As(crew.OtherWorker, "ProductionWorker"))
                .Handle(new GetPerformanceReviewQuery(review.Id), CancellationToken.None))
            .Should().ThrowAsync<KeyNotFoundException>();
        await FluentActions.Awaiting(() => new SubmitSelfReviewHandler(_h.Db, TestCaller.As(crew.OtherWorker), _h.Clock.Object)
                .Handle(new SubmitSelfReviewCommand(review.Id, new SubmitSelfReviewRequestModel(null, "x")), CancellationToken.None))
            .Should().ThrowAsync<KeyNotFoundException>();

        var mine = await new GetPerformanceReviewsHandler(_h.Db, TestCaller.As(crew.OtherWorker, "ProductionWorker"))
            .Handle(new GetPerformanceReviewsQuery(View: GetPerformanceReviewsHandler.ViewMine), CancellationToken.None);
        mine.Should().ContainSingle().Which.EmployeeId.Should().Be(crew.OtherWorker);
    }

    [Fact]
    public async Task A_manager_sees_their_team_but_not_other_teams()
    {
        var crew = await SeedCrewAsync();
        await LaunchAsync(crew);

        var visible = await new GetPerformanceReviewsHandler(_h.Db, TestCaller.As(crew.Manager, "Manager"))
            .Handle(new GetPerformanceReviewsQuery(), CancellationToken.None);

        visible.Select(r => r.EmployeeId).Should().BeEquivalentTo([crew.Manager, crew.Worker, crew.OtherWorker]);
        var outsiderReview = await ReviewOfAsync(crew.Outsider);
        await FluentActions.Awaiting(() => new GetPerformanceReviewHandler(_h.Db, TestCaller.As(crew.Manager, "Manager"))
                .Handle(new GetPerformanceReviewQuery(outsiderReview.Id), CancellationToken.None))
            .Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task The_employee_sees_the_manager_ratings_only_after_submission()
    {
        var crew = await SeedCrewAsync();
        await LaunchAsync(crew);
        var review = await ReviewOfAsync(crew.Worker);
        await new SubmitSelfReviewHandler(_h.Db, TestCaller.As(crew.Worker), _h.Clock.Object).Handle(
            new SubmitSelfReviewCommand(review.Id, new SubmitSelfReviewRequestModel(null, "Mine")), CancellationToken.None);
        await new UpdatePerformanceReviewHandler(_h.Db, TestCaller.As(crew.Manager, "Manager")).Handle(
            new UpdatePerformanceReviewCommand(review.Id, null, 3m, null, null, "Draft strengths", null, null), CancellationToken.None);

        var draftView = await new GetPerformanceReviewHandler(_h.Db, TestCaller.As(crew.Worker))
            .Handle(new GetPerformanceReviewQuery(review.Id), CancellationToken.None);

        draftView.Review.OverallRating.Should().BeNull();
        draftView.Review.StrengthsComments.Should().BeNull();
        draftView.History.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Setting_the_status_directly_is_refused()
    {
        var crew = await SeedCrewAsync();
        await LaunchAsync(crew);
        var review = await ReviewOfAsync(crew.Worker);

        await FluentActions.Awaiting(() => new UpdatePerformanceReviewHandler(_h.Db, TestCaller.As(crew.Admin, "Admin")).Handle(
                new UpdatePerformanceReviewCommand(review.Id, ReviewStatus.Closed, null, null, null, null, null, null), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Only_the_employee_acknowledges_and_a_non_admin_cannot_close_early()
    {
        var crew = await SeedCrewAsync();
        await LaunchAsync(crew);
        var review = await ReviewOfAsync(crew.Worker);

        await FluentActions.Awaiting(() => new AcknowledgeReviewHandler(_h.Db, TestCaller.As(crew.Manager, "Manager"), _h.Clock.Object)
                .Handle(new AcknowledgeReviewCommand(review.Id, new AcknowledgeReviewRequestModel(null)), CancellationToken.None))
            .Should().ThrowAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => new ClosePerformanceReviewHandler(_h.Db, TestCaller.As(crew.Manager, "Manager"), _h.Clock.Object)
                .Handle(new ClosePerformanceReviewCommand(review.Id), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();

        await new ClosePerformanceReviewHandler(_h.Db, TestCaller.As(crew.Admin, "Admin"), _h.Clock.Object)
            .Handle(new ClosePerformanceReviewCommand(review.Id), CancellationToken.None);
        review.Status.Should().Be(ReviewStatus.Closed);
    }

    [Fact]
    public async Task Closing_the_cycle_closes_every_open_review()
    {
        var crew = await SeedCrewAsync();
        var cycleId = await LaunchAsync(crew);

        await new CloseReviewCycleHandler(_h.Db, _h.Clock.Object).Handle(new CloseReviewCycleCommand(cycleId), CancellationToken.None);

        (await _h.Db.PerformanceReviews.Where(r => r.CycleId == cycleId).AllAsync(r => r.Status == ReviewStatus.Closed)).Should().BeTrue();
        var cycle = await new GetReviewCycleHandler(_h.Db).Handle(new GetReviewCycleQuery(cycleId), CancellationToken.None);
        cycle.Status.Should().Be(ReviewCycleStatus.Completed);
        cycle.Progress!.Closed.Should().Be(4);
    }
}
