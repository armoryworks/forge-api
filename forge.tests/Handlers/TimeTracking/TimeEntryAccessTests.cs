using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.TimeTracking;
using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.TimeTracking;

public class TimeEntryAccessTests
{
    private readonly TimerTestHarness _h = new();

    private async Task<(int Ben, int Rosa, TimeEntry RosaEntry)> SeedAsync()
    {
        var ben = await _h.AddUserAsync();
        var rosa = await _h.AddUserAsync();
        var job = await _h.AddJobAsync("J-100");
        var today = DateOnly.FromDateTime(_h.Now.UtcDateTime);
        _h.Db.TimeEntries.Add(new TimeEntry { UserId = ben.Id, Date = today, DurationMinutes = 60, JobId = job.Id });
        var rosaEntry = new TimeEntry { UserId = rosa.Id, Date = today, DurationMinutes = 240, Notes = "IMM-220 run", JobId = job.Id };
        _h.Db.TimeEntries.Add(rosaEntry);
        _h.Db.ClockEvents.AddRange(
            new ClockEvent { UserId = ben.Id, EventTypeCode = "ClockIn", Timestamp = _h.Now.AddHours(-2) },
            new ClockEvent { UserId = rosa.Id, EventTypeCode = "ClockIn", Timestamp = _h.Now.AddHours(-3) });
        await _h.Db.SaveChangesAsync();
        return (ben.Id, rosa.Id, rosaEntry);
    }

    private Task<List<TimeEntryResponseModel>> EntriesAs(int callerId, string[] roles, int? userId = null, int? jobId = null)
        => new GetTimeEntriesHandler(_h.Repo, TestCaller.As(callerId, roles))
            .Handle(new GetTimeEntriesQuery(userId, jobId, null, null), CancellationToken.None);

    [Fact]
    public async Task A_worker_listing_entries_without_a_user_sees_only_their_own()
    {
        var (ben, _, _) = await SeedAsync();

        var entries = await EntriesAs(ben, []);

        entries.Should().NotBeEmpty().And.OnlyContain(e => e.UserId == ben);
    }

    [Fact]
    public async Task A_worker_asking_for_another_users_entries_is_refused()
    {
        var (ben, rosa, _) = await SeedAsync();

        var act = () => EntriesAs(ben, [], userId: rosa);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task A_manager_lists_everyone_and_a_job_shows_all_its_labour()
    {
        var (ben, rosa, entry) = await SeedAsync();

        (await EntriesAs(rosa, ["Manager"])).Select(e => e.UserId).Should().BeEquivalentTo([ben, rosa]);
        (await EntriesAs(ben, [], jobId: entry.JobId)).Select(e => e.UserId).Should().BeEquivalentTo([ben, rosa]);
    }

    [Fact]
    public async Task Clock_events_are_scoped_to_the_caller_unless_supervisor()
    {
        var (ben, rosa, _) = await SeedAsync();
        Task<List<ClockEventResponseModel>> Events(int caller, string[] roles, int? userId)
            => new GetClockEventsHandler(_h.Repo, TestCaller.As(caller, roles))
                .Handle(new GetClockEventsQuery(userId, null, null), CancellationToken.None);

        (await Events(ben, [], null)).Should().ContainSingle().Which.UserId.Should().Be(ben);
        await FluentActions.Awaiting(() => Events(ben, [], rosa)).Should().ThrowAsync<ForbiddenException>();
        (await Events(ben, ["Admin"], null)).Should().HaveCount(2);
    }

    [Fact]
    public async Task A_worker_cannot_edit_another_workers_entry_but_a_manager_can()
    {
        var (ben, _, entry) = await SeedAsync();
        Task Patch(int caller, string[] roles) =>
            new UpdateTimeEntryHandler(_h.Repo, _h.Db, _h.Clock.Object, TestCaller.As(caller, roles)).Handle(
                new UpdateTimeEntryCommand(entry.Id, new UpdateTimeEntryRequestModel(null, null, 30, null, "edited")),
                CancellationToken.None);

        await FluentActions.Awaiting(() => Patch(ben, [])).Should().ThrowAsync<ForbiddenException>();
        (await _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == entry.Id)).DurationMinutes.Should().Be(240);

        await Patch(ben, ["Manager"]);
        (await _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == entry.Id)).DurationMinutes.Should().Be(30);
    }

    [Fact]
    public async Task A_manual_entry_keeps_the_billable_flag_it_was_sent()
    {
        var ben = await _h.AddUserAsync();
        var today = DateOnly.FromDateTime(_h.Now.UtcDateTime);
        var handler = new CreateTimeEntryHandler(_h.Repo, TestCaller.As(ben.Id), _h.Db);

        var indirect = await handler.Handle(new CreateTimeEntryCommand(
            new CreateTimeEntryRequestModel(null, today, 30, "Meeting", null, IsBillable: false)), CancellationToken.None);
        var standard = await handler.Handle(new CreateTimeEntryCommand(
            new CreateTimeEntryRequestModel(null, today, 30, "Production", null)), CancellationToken.None);

        indirect.IsBillable.Should().BeFalse();
        standard.IsBillable.Should().BeTrue();
        (await _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == indirect.Id)).IsBillable.Should().BeFalse();
    }
}
