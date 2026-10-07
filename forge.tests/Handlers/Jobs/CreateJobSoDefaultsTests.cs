using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.SalesOrders.Acceptance;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class CreateJobSoDefaultsTests
{
    private const int StageId = 7;

    private static readonly DateTimeOffset RequestedDelivery = new(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<IJobRepository> _jobRepo = new();
    private readonly Mock<ITrackTypeRepository> _trackRepo = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly CreateJobHandler _handler;

    private Job? _created;

    public CreateJobSoDefaultsTests()
    {
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        var boardHub = new Mock<IHubContext<BoardHub>>();
        boardHub.Setup(h => h.Clients).Returns(clients.Object);

        _trackRepo.Setup(r => r.FindFirstActiveStageAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobStage { Id = StageId, TrackTypeId = 1, Name = "Quote" });
        _jobRepo.Setup(r => r.GenerateNextJobNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("JOB-0500");
        _jobRepo.Setup(r => r.GetMaxBoardPositionAsync(StageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _jobRepo.Setup(r => r.AddAsync(It.IsAny<Job>(), It.IsAny<CancellationToken>()))
            .Callback<Job, CancellationToken>((job, _) => _created = job)
            .Returns(Task.CompletedTask);
        _mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobDetailResponseModel(
                1, "JOB-0500", "Test", null, 1, "Production",
                StageId, "Quote", "#94a3b8", null, null, null, null,
                "Normal", null, null, null, null, null, false, 1, 0, null,
                null, null, null, null, null, null, null, null, null, null, 0,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        _handler = new CreateJobHandler(
            _jobRepo.Object, _trackRepo.Object, _mediator.Object, boardHub.Object,
            Mock.Of<IBarcodeService>(),
            Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
            _db,
            new SalesOrderAcceptanceGate(_db, StubCapabilitySnapshotProvider.Off),
            Mock.Of<ICloudFolderAutoCreator>(),
            Mock.Of<ISystemSettingRepository>(),
            Mock.Of<IBusinessIdentifierService>(),
            StubCapabilitySnapshotProvider.Off);
    }

    private async Task<(SalesOrderLine Line, Part Part)> SeedLineAsync(decimal lineQuantity, int? partId = 600, decimal shipped = 0m)
    {
        var part = new Part { Id = 600, PartNumber = "40-1700M", Description = "Clutch weight" };
        _db.Parts.Add(part);
        var so = new SalesOrder
        {
            OrderNumber = "SO-500",
            CustomerId = 42,
            Status = SalesOrderStatus.Confirmed,
            RequestedDeliveryDate = RequestedDelivery,
        };
        _db.SalesOrders.Add(so);
        await _db.SaveChangesAsync();

        var line = new SalesOrderLine
        {
            SalesOrderId = so.Id,
            PartId = partId,
            Description = "Clutch weights",
            Quantity = lineQuantity,
            ShippedQuantity = shipped,
            UnitPrice = 3m,
            LineNumber = 1,
        };
        _db.SalesOrderLines.Add(line);
        await _db.SaveChangesAsync();
        return (line, part);
    }

    private async Task SeedLinkedJobAsync(
        int lineId, decimal quantity, bool archived = false, JobDisposition? disposition = null, bool completed = false)
    {
        var job = new Job
        {
            JobNumber = $"JOB-L{Guid.NewGuid():N}"[..12],
            Title = "Earlier job",
            TrackTypeId = 1,
            CurrentStageId = StageId,
            PartId = 600,
            SalesOrderLineId = lineId,
            IsArchived = archived,
            Disposition = disposition,
            CompletedDate = completed ? RequestedDelivery : null,
        };
        job.JobParts.Add(new JobPart { PartId = 600, Quantity = quantity });
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
    }

    private static CreateJobCommand ForLine(int lineId) =>
        new("From the line", null, 1, null, null, null, null, SalesOrderLineId: lineId);

    [Fact]
    public async Task A_job_from_only_a_line_takes_its_part_remaining_quantity_customer_and_due_date()
    {
        var (line, part) = await SeedLineAsync(100m, shipped: 10m);
        await SeedLinkedJobAsync(line.Id, 30m);

        await _handler.Handle(ForLine(line.Id), CancellationToken.None);

        _created.Should().NotBeNull();
        _created!.PartId.Should().Be(part.Id);
        _created.CustomerId.Should().Be(42);
        _created.DueDate.Should().Be(RequestedDelivery);
        _created.SalesOrderLineId.Should().Be(line.Id);
        _created.JobParts.Should().ContainSingle()
            .Which.Should().Match<JobPart>(jp => jp.PartId == part.Id && jp.Quantity == 60m);
    }

    [Fact]
    public async Task Archived_and_disposed_jobs_do_not_reduce_the_remaining_quantity()
    {
        var (line, _) = await SeedLineAsync(100m);
        await SeedLinkedJobAsync(line.Id, 30m, archived: true);
        await SeedLinkedJobAsync(line.Id, 20m, disposition: JobDisposition.EnteredInError);

        await _handler.Handle(ForLine(line.Id), CancellationToken.None);

        _created!.JobParts.Single().Quantity.Should().Be(100m);
    }

    [Fact]
    public async Task The_defaulted_quantity_is_at_least_one()
    {
        var (line, _) = await SeedLineAsync(10m);
        await SeedLinkedJobAsync(line.Id, 15m);

        await _handler.Handle(ForLine(line.Id), CancellationToken.None);

        _created!.JobParts.Single().Quantity.Should().Be(1m);
    }

    [Fact]
    public async Task A_completed_job_whose_output_shipped_is_not_counted_twice()
    {
        var (line, _) = await SeedLineAsync(100m, shipped: 50m);
        await SeedLinkedJobAsync(line.Id, 50m, completed: true);

        await _handler.Handle(ForLine(line.Id), CancellationToken.None);

        _created!.JobParts.Single().Quantity.Should().Be(50m);
    }

    [Fact]
    public async Task A_part_other_than_the_lines_does_not_take_the_lines_quantity()
    {
        var (line, _) = await SeedLineAsync(100m);
        _db.Parts.Add(new Part { Id = 601, PartNumber = "40-1800M", Description = "Spacer" });
        await _db.SaveChangesAsync();

        await _handler.Handle(ForLine(line.Id) with { PartId = 601 }, CancellationToken.None);

        _created!.JobParts.Single().Should().Match<JobPart>(jp => jp.PartId == 601 && jp.Quantity == 1m);
    }

    [Fact]
    public async Task A_line_in_other_units_than_the_part_stocks_does_not_set_the_quantity()
    {
        var (line, part) = await SeedLineAsync(100m);
        line.UomId = 2;
        part.StockUomId = 1;
        await _db.SaveChangesAsync();

        await _handler.Handle(ForLine(line.Id), CancellationToken.None);

        _created!.JobParts.Single().Quantity.Should().Be(1m);
    }

    [Fact]
    public async Task Values_the_caller_supplied_win_over_the_line()
    {
        var (line, _) = await SeedLineAsync(100m);
        _db.Parts.Add(new Part { Id = 601, PartNumber = "40-1800M", Description = "Spacer" });
        await _db.SaveChangesAsync();
        var due = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);

        await _handler.Handle(
            new CreateJobCommand("Mine", null, 1, null, 7, null, due,
                PartId: 601, SalesOrderLineId: line.Id, Quantity: 12m),
            CancellationToken.None);

        _created!.PartId.Should().Be(601);
        _created.CustomerId.Should().Be(7);
        _created.DueDate.Should().Be(due);
        _created.JobParts.Single().Should().Match<JobPart>(jp => jp.PartId == 601 && jp.Quantity == 12m);
    }

    [Fact]
    public async Task A_line_without_a_part_still_fills_customer_and_due_date()
    {
        var (line, _) = await SeedLineAsync(5m, partId: null);

        await _handler.Handle(ForLine(line.Id), CancellationToken.None);

        _created!.PartId.Should().BeNull();
        _created.JobParts.Should().BeEmpty();
        _created.CustomerId.Should().Be(42);
        _created.DueDate.Should().Be(RequestedDelivery);
    }

    [Fact]
    public async Task A_quantity_on_a_partless_line_without_a_part_is_rejected()
    {
        var (line, _) = await SeedLineAsync(5m, partId: null);

        var act = () => _handler.Handle(ForLine(line.Id) with { Quantity = 3m }, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage($"*{CreateJobCommandValidator.QuantityNeedsPartMessage}*");
        _created.Should().BeNull();
    }

    [Fact]
    public void The_validator_lets_a_line_supply_the_part_for_a_quantity()
    {
        var validator = new CreateJobCommandValidator();

        validator.Validate(ForLine(1) with { Quantity = 3m }).IsValid.Should().BeTrue();
        validator.Validate(new CreateJobCommand("No line", null, 1, null, null, null, null, Quantity: 3m))
            .IsValid.Should().BeFalse();
    }
}
