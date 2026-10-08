using FluentAssertions;

using Forge.Api.Features.Mobile;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Mobile;

public class GetMyJobsTests
{
    private readonly TimerTestHarness _h = new();
    private JobStage _stage = null!;

    private async Task<int> SeedAsync()
    {
        var track = new TrackType { Name = "Production", Code = "production", IsActive = true };
        _h.Db.TrackTypes.Add(track);
        await _h.Db.SaveChangesAsync();
        _stage = new JobStage { TrackTypeId = track.Id, Name = "Machining", Code = "machining", SortOrder = 1, IsActive = true };
        _h.Db.JobStages.Add(_stage);
        await _h.Db.SaveChangesAsync();
        return (await _h.AddUserAsync()).Id;
    }

    private async Task<Job> AddJobAsync(string number, int? assigneeId, Action<Job>? configure = null)
    {
        var job = new Job
        {
            JobNumber = number,
            Title = $"Title {number}",
            TrackTypeId = _stage.TrackTypeId,
            CurrentStageId = _stage.Id,
            AssigneeId = assigneeId,
        };
        configure?.Invoke(job);
        _h.Db.Jobs.Add(job);
        await _h.Db.SaveChangesAsync();
        return job;
    }

    private Task<List<Forge.Core.Models.MyJobResponseModel>> MineAsync(int userId) =>
        new GetMyJobsHandler(_h.Db, _h.SignedIn(userId), _h.Clock.Object)
            .Handle(new GetMyJobsQuery(), CancellationToken.None);

    [Fact]
    public async Task Lists_only_the_callers_open_assigned_work_orders()
    {
        var me = await SeedAsync();
        var someoneElse = (await _h.AddUserAsync()).Id;
        var open = await AddJobAsync("JOB-1001", me);
        await AddJobAsync("JOB-1002", someoneElse);
        await AddJobAsync("JOB-1003", null);
        await AddJobAsync("JOB-1004", me, j => j.IsArchived = true);
        await AddJobAsync("JOB-1005", me, j => j.CompletedDate = _h.Now.AddDays(-1));
        await AddJobAsync("JOB-1006", me, j => j.Disposition = JobDisposition.ShipToCustomer);

        var mine = await MineAsync(me);

        mine.Select(j => j.Id).Should().Equal(open.Id);
        mine[0].JobNumber.Should().Be("JOB-1001");
        mine[0].Title.Should().Be("Title JOB-1001");
        mine[0].StageId.Should().Be(_stage.Id);
        mine[0].StageName.Should().Be("Machining");
    }

    [Fact]
    public async Task Carries_the_part_number_and_the_quantity_being_made()
    {
        var me = await SeedAsync();
        var part = new Part { PartNumber = "BRK-100", Name = "Bracket" };
        _h.Db.Parts.Add(part);
        await _h.Db.SaveChangesAsync();
        var job = await AddJobAsync("JOB-2001", me, j => j.PartId = part.Id);
        _h.Db.JobParts.Add(new JobPart { JobId = job.Id, PartId = part.Id, Quantity = 25m });
        await _h.Db.SaveChangesAsync();

        var mine = await MineAsync(me);

        mine.Single().PartNumber.Should().Be("BRK-100");
        mine.Single().Quantity.Should().Be(25m);
    }

    [Fact]
    public async Task Sorts_by_due_date_with_undated_work_orders_last()
    {
        var me = await SeedAsync();
        await AddJobAsync("JOB-3001", me);
        await AddJobAsync("JOB-3002", me, j => j.DueDate = _h.Now.AddDays(5));
        await AddJobAsync("JOB-3003", me, j => j.DueDate = _h.Now.AddDays(1));

        var mine = await MineAsync(me);

        mine.Select(j => j.JobNumber).Should().Equal("JOB-3003", "JOB-3002", "JOB-3001");
    }

    [Fact]
    public async Task Overdue_is_judged_against_the_shops_local_day()
    {
        var me = await SeedAsync();
        _h.Db.WorkingCalendars.Add(new WorkingCalendar { Name = "Plant", TimeZone = "America/Denver", IsDefault = true });
        await _h.Db.SaveChangesAsync();
        _h.Now = new DateTimeOffset(2026, 10, 8, 1, 0, 0, TimeSpan.Zero);
        await AddJobAsync("JOB-4001", me, j => j.DueDate = new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero));
        await AddJobAsync("JOB-4002", me, j => j.DueDate = new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero));

        var mine = await MineAsync(me);

        mine.Single(j => j.JobNumber == "JOB-4001").IsOverdue.Should().BeFalse();
        mine.Single(j => j.JobNumber == "JOB-4002").IsOverdue.Should().BeTrue();
    }

    [Fact]
    public async Task Flags_the_work_order_the_caller_has_a_timer_running_on()
    {
        var me = await SeedAsync();
        var someoneElse = (await _h.AddUserAsync()).Id;
        var timed = await AddJobAsync("JOB-5001", me);
        var otherTimed = await AddJobAsync("JOB-5002", me);
        var stopped = await AddJobAsync("JOB-5003", me);
        await _h.AddRunningTimerAsync(me, timed.Id, _h.Now.AddMinutes(-30));
        await _h.AddRunningTimerAsync(someoneElse, otherTimed.Id, _h.Now.AddMinutes(-30));
        var finished = await _h.AddRunningTimerAsync(me, stopped.Id, _h.Now.AddHours(-3));
        finished.TimerStop = _h.Now.AddHours(-2);
        await _h.Db.SaveChangesAsync();

        var mine = await MineAsync(me);

        mine.Single(j => j.Id == timed.Id).HasRunningTimer.Should().BeTrue();
        mine.Single(j => j.Id == otherTimed.Id).HasRunningTimer.Should().BeFalse();
        mine.Single(j => j.Id == stopped.Id).HasRunningTimer.Should().BeFalse();
    }

    [Fact]
    public async Task A_caller_with_no_assigned_work_orders_gets_an_empty_list()
    {
        var me = await SeedAsync();

        (await MineAsync(me)).Should().BeEmpty();
    }
}
