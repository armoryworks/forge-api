using FluentAssertions;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Repositories;

[Collection(PostgresCollection.Name)]
public sealed class JobRepositoryTeamFilterTests(PostgresFixture fixture)
{
    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed record Seed(int TrackTypeId, int SawTeamId, int MillTeamId);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private async Task<Seed> SeedAsync()
    {
        await using var db = fixture.CreateContext();

        var track = new TrackType { Name = "Team filter", Code = Unique("team"), IsActive = true };
        var sawTeam = new Team { Name = Unique("Saw team") };
        var millTeam = new Team { Name = Unique("Mill team") };
        var part = new Part { PartNumber = Unique("P"), Description = "Routed part" };
        db.AddRange(track, sawTeam, millTeam, part);
        await db.SaveChangesAsync();

        var stage = new JobStage { TrackTypeId = track.Id, Name = "Production", Code = "prod", SortOrder = 1, IsActive = true };
        var sawCenter = new WorkCenter { Name = "Saw", Code = Unique("WC"), TeamId = sawTeam.Id };
        var millCenter = new WorkCenter { Name = "Mill", Code = Unique("WC"), TeamId = millTeam.Id };
        db.AddRange(stage, sawCenter, millCenter);
        await db.SaveChangesAsync();

        var saw = new Operation { PartId = part.Id, StepNumber = 10, Title = "Saw", WorkCenterId = sawCenter.Id };
        var mill = new Operation { PartId = part.Id, StepNumber = 20, Title = "Mill", WorkCenterId = millCenter.Id };
        db.Operations.AddRange(saw, mill);

        Job NewJob(string title, int? partId) => new()
        {
            JobNumber = Unique("J"),
            Title = title,
            TrackTypeId = track.Id,
            CurrentStageId = stage.Id,
            PartId = partId,
        };

        var untouched = NewJob("untouched", part.Id);
        var sawDone = NewJob("saw-done", part.Id);
        var sawSkipped = NewJob("saw-skipped", part.Id);
        var bothRunning = NewJob("both-running", part.Id);
        var millStarted = NewJob("mill-started-out-of-order", part.Id);
        var allDone = NewJob("all-done", part.Id);
        var partless = NewJob("partless", null);
        db.Jobs.AddRange(untouched, sawDone, sawSkipped, bothRunning, millStarted, allDone, partless);
        await db.SaveChangesAsync();

        JobOperation Row(Job job, Operation op, JobOperationStatus status) => new()
        {
            JobId = job.Id, OperationId = op.Id, StepNumber = op.StepNumber, Title = op.Title, Status = status,
        };

        db.JobOperations.AddRange(
            Row(sawDone, saw, JobOperationStatus.Complete),
            Row(sawSkipped, saw, JobOperationStatus.Skipped),
            Row(bothRunning, saw, JobOperationStatus.InProgress),
            Row(millStarted, saw, JobOperationStatus.NotStarted),
            Row(millStarted, mill, JobOperationStatus.InProgress),
            Row(allDone, saw, JobOperationStatus.Complete),
            Row(allDone, mill, JobOperationStatus.Skipped));
        db.TimeEntries.Add(new TimeEntry
        {
            JobId = bothRunning.Id,
            OperationId = mill.Id,
            UserId = 515151,
            Date = DateOnly.FromDateTime(DateTime.UnixEpoch),
            TimerStart = DateTimeOffset.UnixEpoch,
        });
        await db.SaveChangesAsync();

        return new Seed(track.Id, sawTeam.Id, millTeam.Id);
    }

    private async Task<List<string>> TitlesAsync(Seed seed, int teamId, bool tracking)
    {
        await using var db = fixture.CreateContext();
        var repo = new JobRepository(db, new FixedClock(DateTimeOffset.UtcNow));
        var page = await repo.GetPagedJobsAsync(
            new JobListQuery { TrackTypeId = seed.TrackTypeId, TeamId = teamId, Sort = "title", PageSize = 200 },
            tracking,
            CancellationToken.None);
        page.TotalCount.Should().Be(page.Items.Count);
        return page.Items.Select(j => j.Title).ToList();
    }

    [Fact]
    public async Task Tracking_on_shows_jobs_whose_first_open_step_is_at_the_team()
    {
        var seed = await SeedAsync();

        var titles = await TitlesAsync(seed, seed.SawTeamId, tracking: true);

        titles.Should().Equal("both-running", "untouched");
    }

    [Fact]
    public async Task Tracking_on_moves_a_job_to_the_next_team_once_its_step_is_closed()
    {
        var seed = await SeedAsync();

        var titles = await TitlesAsync(seed, seed.MillTeamId, tracking: true);

        titles.Should().Equal("both-running", "mill-started-out-of-order", "saw-done", "saw-skipped");
    }

    [Fact]
    public async Task Tracking_off_shows_every_job_whose_routing_visits_the_team()
    {
        var seed = await SeedAsync();

        var saw = await TitlesAsync(seed, seed.SawTeamId, tracking: false);
        var mill = await TitlesAsync(seed, seed.MillTeamId, tracking: false);

        var routed = new[] { "all-done", "both-running", "mill-started-out-of-order", "saw-done", "saw-skipped", "untouched" };
        saw.Should().Equal(routed);
        mill.Should().Equal(routed);
    }

    [Fact]
    public async Task A_team_that_owns_no_work_center_matches_nothing()
    {
        var seed = await SeedAsync();
        var idleTeam = new Team { Name = Unique("Idle team") };
        await using (var db = fixture.CreateContext())
        {
            db.Teams.Add(idleTeam);
            await db.SaveChangesAsync();
        }

        (await TitlesAsync(seed, idleTeam.Id, tracking: true)).Should().BeEmpty();
        (await TitlesAsync(seed, idleTeam.Id, tracking: false)).Should().BeEmpty();
    }

    [Fact]
    public async Task Without_a_team_the_board_is_unfiltered()
    {
        var seed = await SeedAsync();
        await using var db = fixture.CreateContext();
        var repo = new JobRepository(db, new FixedClock(DateTimeOffset.UtcNow));

        var page = await repo.GetPagedJobsAsync(
            new JobListQuery { TrackTypeId = seed.TrackTypeId, PageSize = 200 }, true, CancellationToken.None);

        page.Items.Should().HaveCount(7);
    }
}
