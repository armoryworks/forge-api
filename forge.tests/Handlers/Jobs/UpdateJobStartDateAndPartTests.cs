using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class UpdateJobStartDateAndPartTests
{
    private static readonly DateTimeOffset Due = new(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<IJobRepository> _repo = new();
    private readonly Mock<IActivityLogRepository> _activity = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly UpdateJobHandler _handler;
    private readonly List<JobActivityLog> _logged = [];

    public UpdateJobStartDateAndPartTests()
    {
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        var boardHub = new Mock<IHubContext<BoardHub>>();
        boardHub.Setup(h => h.Clients).Returns(clients.Object);

        _repo.Setup(r => r.FindAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns<int, CancellationToken>(async (id, ct) => await _db.Jobs.FindAsync([id], ct));
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(ct => _db.SaveChangesAsync(ct));
        _activity.Setup(a => a.AddAsync(It.IsAny<JobActivityLog>(), It.IsAny<CancellationToken>()))
            .Callback<JobActivityLog, CancellationToken>((log, _) => _logged.Add(log))
            .Returns(Task.CompletedTask);

        _handler = new UpdateJobHandler(
            _repo.Object,
            _activity.Object,
            Mock.Of<IMediator>(),
            boardHub.Object,
            Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
            Mock.Of<ISystemSettingRepository>(),
            Mock.Of<IBusinessIdentifierService>(),
            _db,
            StubCapabilitySnapshotProvider.Off);
    }

    private async Task<Job> SeedJobAsync(int? partId = 700, decimal quantity = 25m)
    {
        _db.Parts.AddRange(
            new Part { Id = 700, PartNumber = "40-1700M", Description = "Clutch weight" },
            new Part { Id = 701, PartNumber = "40-1800M", Description = "Spacer" });
        var job = new Job
        {
            Id = 1, JobNumber = "J-1", Title = "Test", TrackTypeId = 1, CurrentStageId = 1,
            DueDate = Due, PartId = partId,
        };
        if (partId is int p)
            job.JobParts.Add(new JobPart { PartId = p, Quantity = quantity });
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    private static UpdateJobCommand Update(DateTimeOffset? start = null, int? partId = null, DateTimeOffset? due = null) =>
        new(1, null, null, null, null, null, due, null, null, StartDate: start, PartId: partId);

    [Fact]
    public async Task Sets_the_planned_start_and_logs_it()
    {
        var job = await SeedJobAsync();
        var start = Due.AddDays(-5);

        await _handler.Handle(Update(start: start), CancellationToken.None);

        job.StartDate.Should().Be(start);
        _logged.Should().ContainSingle(l => l.FieldName == "StartDate");
    }

    [Fact]
    public async Task A_start_on_the_due_date_is_allowed()
    {
        var job = await SeedJobAsync();

        await _handler.Handle(Update(start: Due.AddHours(15)), CancellationToken.None);

        job.StartDate.Should().Be(Due.AddHours(15));
    }

    [Fact]
    public async Task A_start_after_the_existing_due_date_is_rejected()
    {
        var job = await SeedJobAsync();

        var act = () => _handler.Handle(Update(start: Due.AddDays(1)), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().WithMessage($"*{UpdateJobHandler.StartAfterDueMessage}*");
        job.StartDate.Should().BeNull();
    }

    [Fact]
    public async Task Moving_the_due_date_before_the_existing_start_is_rejected()
    {
        var job = await SeedJobAsync();
        job.StartDate = Due.AddDays(-2);
        await _db.SaveChangesAsync();

        var act = () => _handler.Handle(Update(due: Due.AddDays(-3)), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public void The_validator_rejects_a_start_after_the_due_date_in_the_same_request()
    {
        var validator = new UpdateJobCommandValidator();

        validator.Validate(Update(start: Due.AddDays(1), due: Due)).IsValid.Should().BeFalse();
        validator.Validate(Update(start: Due, due: Due)).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Changing_the_part_before_work_starts_moves_its_quantity_with_it()
    {
        var job = await SeedJobAsync();

        await _handler.Handle(Update(partId: 701), CancellationToken.None);

        job.PartId.Should().Be(701);
        var jobPart = await _db.JobParts.SingleAsync(jp => jp.JobId == 1);
        jobPart.PartId.Should().Be(701);
        jobPart.Quantity.Should().Be(25m);
        _logged.Should().ContainSingle(l => l.FieldName == "Part" && l.OldValue == "40-1700M" && l.NewValue == "40-1800M");
    }

    [Fact]
    public async Task A_job_without_a_part_can_be_given_one()
    {
        var job = await SeedJobAsync(partId: null);

        await _handler.Handle(Update(partId: 700), CancellationToken.None);

        job.PartId.Should().Be(700);
    }

    [Fact]
    public async Task The_part_is_locked_once_the_bom_revision_is_pinned()
    {
        var job = await SeedJobAsync();
        job.BomRevisionIdAtRelease = 9;
        await _db.SaveChangesAsync();

        var act = () => _handler.Handle(Update(partId: 701), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message
            .Should().Be(UpdateJobHandler.PartLockedMessage);
        job.PartId.Should().Be(700);
    }

    [Fact]
    public async Task The_part_is_locked_once_time_is_logged()
    {
        var job = await SeedJobAsync();
        _db.TimeEntries.Add(new TimeEntry { JobId = job.Id, UserId = 1, DurationMinutes = 30 });
        await _db.SaveChangesAsync();

        var act = () => _handler.Handle(Update(partId: 701), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message
            .Should().Be(UpdateJobHandler.PartLockedMessage);
    }

    [Fact]
    public async Task The_part_is_locked_once_a_production_run_exists()
    {
        var job = await SeedJobAsync();
        _db.ProductionRuns.Add(new ProductionRun { JobId = job.Id, PartId = 700, RunNumber = "R-1" });
        await _db.SaveChangesAsync();

        var act = () => _handler.Handle(Update(partId: 701), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message
            .Should().Be(UpdateJobHandler.PartLockedMessage);
    }

    [Fact]
    public async Task An_unknown_part_is_not_found()
    {
        await SeedJobAsync();

        var act = () => _handler.Handle(Update(partId: 9999), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
