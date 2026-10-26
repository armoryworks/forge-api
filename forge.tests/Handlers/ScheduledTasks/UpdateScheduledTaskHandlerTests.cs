using FluentAssertions;
using Moq;

using Forge.Api.Features.ScheduledTasks;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.ScheduledTasks;

public class UpdateScheduledTaskHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Stored = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    private static IClock Clock()
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);
        return clock.Object;
    }

    private static async Task<ScheduledTask> AddTaskAsync(Forge.Data.Context.AppDbContext db, bool isActive = true)
    {
        var trackType = new TrackType { Name = "Maintenance", Code = "MAINT" };
        db.TrackTypes.Add(trackType);
        await db.SaveChangesAsync();
        var task = new ScheduledTask { Name = "Gage review", TrackTypeId = trackType.Id, CronExpression = "0 8 * * 1", IsActive = isActive, NextRunAt = Stored };
        db.ScheduledTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    [Fact]
    public async Task Handle_ChangedCron_MovesTheNextRunToTheNewExpression()
    {
        using var db = TestDbContextFactory.Create();
        var task = await AddTaskAsync(db);

        var result = await new UpdateScheduledTaskHandler(db, Clock())
            .Handle(new UpdateScheduledTaskCommand(task.Id, null, null, null, null, null, "0 6 * * *", null), CancellationToken.None);

        result.NextRunAt.Should().Be(new DateTimeOffset(2026, 10, 29, 6, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Handle_RenameOnly_KeepsTheNextRun()
    {
        using var db = TestDbContextFactory.Create();
        var task = await AddTaskAsync(db);

        var result = await new UpdateScheduledTaskHandler(db, Clock())
            .Handle(new UpdateScheduledTaskCommand(task.Id, "Gage due list", null, null, null, null, "0 8 * * 1", null), CancellationToken.None);

        result.NextRunAt.Should().Be(Stored);
    }

    [Fact]
    public async Task Handle_Reactivated_SkipsTheOccurrencesMissedWhileInactive()
    {
        using var db = TestDbContextFactory.Create();
        var task = await AddTaskAsync(db, isActive: false);
        task.NextRunAt = Now.AddDays(-20);
        await db.SaveChangesAsync();

        var result = await new UpdateScheduledTaskHandler(db, Clock())
            .Handle(new UpdateScheduledTaskCommand(task.Id, null, null, null, null, null, null, true), CancellationToken.None);

        result.NextRunAt.Should().Be(Stored);
    }

    [Fact]
    public void Validator_RejectsAnInvalidCronButAllowsLeavingItOut()
    {
        var validator = new UpdateScheduledTaskValidator();

        validator.Validate(new UpdateScheduledTaskCommand(1, null, null, null, null, null, "soon", null))
            .Errors.Should().ContainSingle(e => e.ErrorCode == "cron-invalid");
        validator.Validate(new UpdateScheduledTaskCommand(1, "x", null, null, null, null, null, null))
            .IsValid.Should().BeTrue();
    }
}
