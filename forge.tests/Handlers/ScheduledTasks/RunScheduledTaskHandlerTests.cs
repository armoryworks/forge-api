using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.ScheduledTasks;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.ScheduledTasks;

public class RunScheduledTaskHandlerTests
{
    [Fact]
    public async Task Handle_CreatesInternalJobNumberedFromTheJobSequence()
    {
        using var db = TestDbContextFactory.Create();
        var trackType = new TrackType { Name = "Maintenance", Code = "MAINT" };
        db.TrackTypes.Add(trackType);
        await db.SaveChangesAsync();
        db.JobStages.Add(new JobStage { Name = "Queued", TrackTypeId = trackType.Id, SortOrder = 1 });
        var task = new ScheduledTask { Name = "Weekly lube", TrackTypeId = trackType.Id, CronExpression = "0 0 * * MON" };
        db.ScheduledTasks.Add(task);
        await db.SaveChangesAsync();

        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(r => r.GenerateNextJobNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("J-77");
        jobRepo.Setup(r => r.GetMaxBoardPositionAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(2);
        var identifiers = new Mock<IBusinessIdentifierService>();
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(now);

        var jobId = await new RunScheduledTaskHandler(db, jobRepo.Object, identifiers.Object, clock.Object)
            .Handle(new RunScheduledTaskCommand(task.Id), CancellationToken.None);

        var job = await db.Jobs.Include(j => j.ActivityLogs).SingleAsync(j => j.Id == jobId);
        job.JobNumber.Should().Be("J-77");
        job.IsInternal.Should().BeTrue();
        job.BoardPosition.Should().Be(3);
        identifiers.Verify(i => i.IssueAsync(BusinessEntityType.Job, job.Id, "J-77", It.IsAny<CancellationToken>()), Times.Once);
        job.ActivityLogs.Should().ContainSingle(l => l.Action == ActivityAction.Created);
        (await db.ScheduledTasks.SingleAsync(t => t.Id == task.Id)).LastRunAt.Should().Be(now);
    }
}
