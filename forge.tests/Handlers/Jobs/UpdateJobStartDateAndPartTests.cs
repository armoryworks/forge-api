using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class UpdateJobStartDateAndPartTests
{
    private static readonly DateTimeOffset Due = new(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<IJobRepository> _repo = new();
    private readonly Mock<IActivityLogRepository> _activity = new();
    private readonly Mock<ISystemSettingRepository> _settings = new();
    private readonly Mock<IBusinessIdentifierService> _identifiers = new();
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
            _settings.Object,
            _identifiers.Object,
            _db,
            StubCapabilitySnapshotProvider.Off);
    }

    private async Task<Job> SeedJobAsync(int? partId = 700, decimal quantity = 25m)
    {
        _db.Parts.AddRange(
            new Part { Id = 700, PartNumber = "40-1700M", Description = "Clutch weight" },
            new Part { Id = 701, PartNumber = "40-1800M", Description = "Spacer", CurrentBomRevisionId = 12 });
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

    private static UpdateJobCommand Update(
        DateTimeOffset? start = null, int? partId = null, DateTimeOffset? due = null, string? jobNumber = null) =>
        new(1, null, null, null, null, null, due, null, null, JobNumber: jobNumber, StartDate: start, PartId: partId);

    private async Task<SalesOrderLine> SeedLineAsync(decimal quantity, decimal shipped)
    {
        var so = new SalesOrder { OrderNumber = "SO-1", CustomerId = 42, Status = SalesOrderStatus.Confirmed };
        _db.SalesOrders.Add(so);
        await _db.SaveChangesAsync();
        var line = new SalesOrderLine
        {
            SalesOrderId = so.Id, PartId = 700, Description = "Clutch weights",
            Quantity = quantity, ShippedQuantity = shipped, UnitPrice = 3m, LineNumber = 1,
        };
        _db.SalesOrderLines.Add(line);
        await _db.SaveChangesAsync();
        return line;
    }

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
    public async Task A_job_without_a_part_can_be_given_one_with_a_quantity_of_one()
    {
        var job = await SeedJobAsync(partId: null);

        await _handler.Handle(Update(partId: 700), CancellationToken.None);

        job.PartId.Should().Be(700);
        var jobPart = await _db.JobParts.SingleAsync(jp => jp.JobId == 1);
        jobPart.PartId.Should().Be(700);
        jobPart.Quantity.Should().Be(1m);
    }

    [Fact]
    public async Task A_line_linked_job_given_a_part_takes_the_lines_remaining_quantity()
    {
        var line = await SeedLineAsync(quantity: 100m, shipped: 10m);
        var other = new Job
        {
            Id = 2, JobNumber = "J-2", Title = "Earlier", TrackTypeId = 1, CurrentStageId = 1,
            PartId = 700, SalesOrderLineId = line.Id,
        };
        other.JobParts.Add(new JobPart { PartId = 700, Quantity = 30m });
        _db.Jobs.Add(other);
        var job = await SeedJobAsync(partId: null);
        job.SalesOrderLineId = line.Id;
        await _db.SaveChangesAsync();

        await _handler.Handle(Update(partId: 700), CancellationToken.None);

        var jobPart = await _db.JobParts.SingleAsync(jp => jp.JobId == 1);
        jobPart.Quantity.Should().Be(60m);
    }

    [Fact]
    public async Task A_pinned_bom_revision_does_not_lock_the_part_and_follows_the_new_part()
    {
        var job = await SeedJobAsync();
        job.BomRevisionIdAtRelease = 9;
        await _db.SaveChangesAsync();

        await _handler.Handle(Update(partId: 701), CancellationToken.None);

        job.PartId.Should().Be(701);
        job.BomRevisionIdAtRelease.Should().Be(12);
    }

    [Fact]
    public async Task A_new_part_without_a_bom_clears_the_pin()
    {
        var job = await SeedJobAsync(partId: 701);
        job.BomRevisionIdAtRelease = 12;
        await _db.SaveChangesAsync();

        await _handler.Handle(Update(partId: 700), CancellationToken.None);

        job.BomRevisionIdAtRelease.Should().BeNull();
    }

    [Fact]
    public async Task A_locked_part_change_leaves_the_job_number_registry_untouched()
    {
        var job = await SeedJobAsync();
        _db.TimeEntries.Add(new TimeEntry { JobId = job.Id, UserId = 1, DurationMinutes = 30 });
        await _db.SaveChangesAsync();
        _settings.Setup(s => s.FindByKeyAsync("jobs.allow_manual_numbers", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSetting { Key = "jobs.allow_manual_numbers", Value = "true" });

        var act = () => _handler.Handle(Update(partId: 701, jobNumber: "J-NEW"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        job.JobNumber.Should().Be("J-1");
        _identifiers.VerifyNoOtherCalls();
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
