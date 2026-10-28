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

public class UpdateJobQuantityTests
{
    private readonly Mock<IJobRepository> _repo = new();
    private readonly Mock<IActivityLogRepository> _activity = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly UpdateJobHandler _handler;
    private readonly List<JobActivityLog> _logged = [];

    public UpdateJobQuantityTests()
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

    private async Task<Job> SeedJobAsync(int? partId = 700, decimal? quantity = 1m)
    {
        _db.Parts.AddRange(
            new Part { Id = 700, PartNumber = "40-1700M", Description = "Clutch weight" },
            new Part { Id = 701, PartNumber = "40-1800M", Description = "Spacer" });
        var job = new Job
        {
            Id = 1, JobNumber = "J-1", Title = "Test", TrackTypeId = 1, CurrentStageId = 1, PartId = partId,
        };
        if (partId is int p && quantity is decimal q)
            job.JobParts.Add(new JobPart { PartId = p, Quantity = q });
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    private static UpdateJobCommand Update(decimal? quantity, int? partId = null) =>
        new(1, null, null, null, null, null, null, null, null, PartId: partId, Quantity: quantity);

    [Fact]
    public async Task Sets_the_build_quantity_and_logs_the_change()
    {
        await SeedJobAsync();

        await _handler.Handle(Update(300m), CancellationToken.None);

        (await _db.JobParts.SingleAsync(jp => jp.JobId == 1)).Quantity.Should().Be(300m);
        _logged.Should().ContainSingle(l => l.FieldName == "Quantity"
            && l.Action == ActivityAction.FieldChanged && l.OldValue == "1" && l.NewValue == "300");
    }

    [Fact]
    public async Task An_unchanged_quantity_logs_nothing()
    {
        await SeedJobAsync(quantity: 300m);

        await _handler.Handle(Update(300m), CancellationToken.None);

        _logged.Should().BeEmpty();
    }

    [Fact]
    public async Task Leaves_other_parts_on_the_job_alone()
    {
        var job = await SeedJobAsync();
        _db.JobParts.Add(new JobPart { JobId = job.Id, PartId = 701, Quantity = 4m });
        await _db.SaveChangesAsync();

        await _handler.Handle(Update(50m), CancellationToken.None);

        (await _db.JobParts.SingleAsync(jp => jp.PartId == 700)).Quantity.Should().Be(50m);
        (await _db.JobParts.SingleAsync(jp => jp.PartId == 701)).Quantity.Should().Be(4m);
    }

    [Fact]
    public async Task A_part_without_a_job_part_row_gets_one()
    {
        await SeedJobAsync(quantity: null);

        await _handler.Handle(Update(12m), CancellationToken.None);

        (await _db.JobParts.SingleAsync(jp => jp.JobId == 1)).Quantity.Should().Be(12m);
        _logged.Should().ContainSingle(l => l.FieldName == "Quantity" && l.OldValue == null && l.NewValue == "12");
    }

    [Fact]
    public async Task A_quantity_sent_with_a_part_change_applies_to_the_new_part()
    {
        await SeedJobAsync(quantity: 25m);

        await _handler.Handle(Update(80m, partId: 701), CancellationToken.None);

        var jobPart = await _db.JobParts.SingleAsync(jp => jp.JobId == 1);
        jobPart.PartId.Should().Be(701);
        jobPart.Quantity.Should().Be(80m);
        _logged.Select(l => l.FieldName).Should().Equal("Part", "Quantity");
    }

    [Fact]
    public async Task A_job_without_a_part_cannot_take_a_quantity()
    {
        await SeedJobAsync(partId: null);

        var act = () => _handler.Handle(Update(5m), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage($"*{CreateJobCommandValidator.QuantityNeedsPartMessage}*");
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("disposed")]
    [InlineData("archived")]
    public async Task A_finished_job_keeps_its_quantity(string state)
    {
        var job = await SeedJobAsync();
        switch (state)
        {
            case "completed": job.CompletedDate = DateTimeOffset.UtcNow; break;
            case "disposed": job.Disposition = JobDisposition.ShipToCustomer; break;
            case "archived": job.IsArchived = true; break;
        }
        await _db.SaveChangesAsync();

        var act = () => _handler.Handle(Update(5m), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message
            .Should().Be(UpdateJobHandler.QuantityLockedMessage);
        (await _db.JobParts.SingleAsync(jp => jp.JobId == 1)).Quantity.Should().Be(1m);
    }

    [Fact]
    public async Task Quantity_can_change_after_work_has_started()
    {
        await SeedJobAsync();
        _db.TimeEntries.Add(new TimeEntry { JobId = 1, UserId = 1, DurationMinutes = 30 });
        await _db.SaveChangesAsync();

        await _handler.Handle(Update(40m), CancellationToken.None);

        (await _db.JobParts.SingleAsync(jp => jp.JobId == 1)).Quantity.Should().Be(40m);
    }

    [Fact]
    public async Task An_exploded_job_keeps_its_quantity()
    {
        await SeedJobAsync();
        _db.Jobs.Add(new Job { Id = 2, JobNumber = "J-2", Title = "Sub", TrackTypeId = 1, CurrentStageId = 1, ParentJobId = 1 });
        await _db.SaveChangesAsync();

        var act = () => _handler.Handle(Update(300m), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message
            .Should().Be(UpdateJobHandler.QuantityExplodedMessage);
        (await _db.JobParts.SingleAsync(jp => jp.JobId == 1)).Quantity.Should().Be(1m);
    }

    [Fact]
    public async Task Sub_jobs_entered_in_error_do_not_lock_the_quantity()
    {
        await SeedJobAsync();
        _db.Jobs.Add(new Job
        {
            Id = 2, JobNumber = "J-2", Title = "Sub", TrackTypeId = 1, CurrentStageId = 1, ParentJobId = 1,
            Disposition = JobDisposition.EnteredInError,
        });
        await _db.SaveChangesAsync();

        await _handler.Handle(Update(300m), CancellationToken.None);

        (await _db.JobParts.SingleAsync(jp => jp.JobId == 1)).Quantity.Should().Be(300m);
    }

    [Fact]
    public async Task Resending_the_current_quantity_on_a_finished_job_is_not_refused()
    {
        var job = await SeedJobAsync(quantity: 300m);
        job.CompletedDate = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        await _handler.Handle(Update(300m), CancellationToken.None);

        _logged.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void The_validator_rejects_a_quantity_that_is_not_positive(decimal quantity)
    {
        new UpdateJobCommandValidator().Validate(Update(quantity)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void The_validator_allows_leaving_the_quantity_out()
    {
        new UpdateJobCommandValidator().Validate(Update(null)).IsValid.Should().BeTrue();
    }

    private static UpdateJobCommand Extras(int? extra, decimal? quantity = null, bool clear = false) =>
        new(1, null, null, null, null, null, null, null, null, Quantity: quantity, ExtraPieces: extra,
            ClearExtraPieces: clear ? true : null);

    [Fact]
    public async Task Extra_pieces_make_the_current_quantity_the_order_and_start_the_order_plus_extras()
    {
        await SeedJobAsync(quantity: 100m);

        await _handler.Handle(Extras(14), CancellationToken.None);

        var job = await _db.Jobs.SingleAsync(j => j.Id == 1);
        job.TargetQuantity.Should().Be(100m);
        job.ExtraPieces.Should().Be(14);
        (await _db.JobParts.SingleAsync(jp => jp.JobId == 1)).Quantity.Should().Be(114m);
        _logged.Select(l => l.FieldName).Should().Equal("TargetQuantity", "ExtraPieces", "Quantity");
        _logged.Single(l => l.FieldName == "ExtraPieces").Description.Should().Be("Extra pieces set to 14.");
    }

    [Fact]
    public async Task With_extra_pieces_the_quantity_sent_is_the_order_quantity()
    {
        var job = await SeedJobAsync(quantity: 114m);
        job.TargetQuantity = 100m;
        job.ExtraPieces = 14;
        await _db.SaveChangesAsync();

        await _handler.Handle(Extras(10, quantity: 200m), CancellationToken.None);

        job.TargetQuantity.Should().Be(200m);
        job.ExtraPieces.Should().Be(10);
        (await _db.JobParts.SingleAsync(jp => jp.JobId == 1)).Quantity.Should().Be(210m);
        _logged.Single(l => l.FieldName == "ExtraPieces").Description.Should().Be("Extra pieces changed from 14 to 10.");
    }

    [Fact]
    public async Task Clearing_extra_pieces_returns_an_order_job_to_the_calculated_plan()
    {
        var job = await SeedJobAsync(quantity: 114m);
        var part = await _db.Parts.SingleAsync(p => p.Id == 700);
        part.ExpectedYieldPercent = 95m;
        var order = new SalesOrder { OrderNumber = "SO-1", CustomerId = 1, Status = SalesOrderStatus.Confirmed };
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();
        var line = new SalesOrderLine { SalesOrderId = order.Id, PartId = 700, Description = "x", Quantity = 100m, LineNumber = 1 };
        _db.SalesOrderLines.Add(line);
        await _db.SaveChangesAsync();
        job.SalesOrderLineId = line.Id;
        job.TargetQuantity = 100m;
        job.ExtraPieces = 14;
        await _db.SaveChangesAsync();

        await _handler.Handle(Extras(null, clear: true), CancellationToken.None);

        job.ExtraPieces.Should().BeNull();
        job.TargetQuantity.Should().Be(100m);
        (await _db.JobParts.SingleAsync(jp => jp.JobId == 1)).Quantity.Should().Be(106m);
        _logged.Single(l => l.FieldName == "ExtraPieces").Description
            .Should().Be("Extra pieces cleared (was 14); the start quantity uses the calculated plan.");
    }

    [Fact]
    public async Task Unchanged_extra_pieces_log_nothing()
    {
        var job = await SeedJobAsync(quantity: 114m);
        job.TargetQuantity = 100m;
        job.ExtraPieces = 14;
        await _db.SaveChangesAsync();

        await _handler.Handle(Extras(14), CancellationToken.None);

        _logged.Should().BeEmpty();
    }

    [Fact]
    public async Task A_finished_job_keeps_its_extra_pieces()
    {
        var job = await SeedJobAsync(quantity: 100m);
        job.CompletedDate = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        var act = () => _handler.Handle(Extras(5), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message
            .Should().Be(UpdateJobHandler.QuantityLockedMessage);
        job.ExtraPieces.Should().BeNull();
    }

    [Fact]
    public async Task A_job_without_a_part_cannot_take_extra_pieces()
    {
        await SeedJobAsync(partId: null);

        var act = () => _handler.Handle(Extras(5), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage($"*{CreateJobCommandValidator.ExtraPiecesNeedPartMessage}*");
    }

    [Theory]
    [InlineData(-1, false, false)]
    [InlineData(3, true, false)]
    [InlineData(0, false, true)]
    [InlineData(null, true, true)]
    public void Extra_pieces_validation(int? extra, bool clear, bool valid)
        => new UpdateJobCommandValidator().Validate(Extras(extra, clear: clear)).IsValid.Should().Be(valid);
}
