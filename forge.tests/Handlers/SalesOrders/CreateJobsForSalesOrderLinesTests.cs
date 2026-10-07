using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.SalesOrders;
using Forge.Api.Features.SalesOrders.Acceptance;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SalesOrders;

public class CreateJobsForSalesOrderLinesTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IJobRepository> _jobRepo = new();
    private readonly Mock<IBarcodeService> _barcodes = new();
    private readonly Mock<IBusinessIdentifierService> _identifiers = new();
    private readonly Mock<IHubContext<BoardHub>> _boardHub = new();
    private readonly Mock<IClientProxy> _boardClients = new();
    private readonly Mock<ISalesOrderAcceptanceGate> _acceptanceGate = new();
    private int _nextJobNumber = 9001;

    public CreateJobsForSalesOrderLinesTests()
    {
        _jobRepo.Setup(r => r.GenerateNextJobNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"J-{_nextJobNumber++}");
        _jobRepo.Setup(r => r.GetMaxBoardPositionAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(_boardClients.Object);
        _boardHub.SetupGet(h => h.Clients).Returns(clients.Object);
    }

    private CreateJobsForSalesOrderLinesHandler Handler() => new(
        _db, _jobRepo.Object, _barcodes.Object, _identifiers.Object, _boardHub.Object, _acceptanceGate.Object);

    private void SeedTrack(bool withOrderConfirmedStage = true)
    {
        _db.TrackTypes.Add(new TrackType { Id = 7, Name = "Production", IsDefault = true, IsActive = true });
        _db.JobStages.Add(new JobStage
        {
            Id = 70, TrackTypeId = 7, Name = "Quote Requested", Code = "quote_requested",
            SortOrder = 1, IsActive = true,
        });
        if (withOrderConfirmedStage)
        {
            _db.JobStages.Add(new JobStage
            {
                Id = 73, TrackTypeId = 7, Name = "Order Confirmed", Code = "order_confirmed",
                SortOrder = 3, IsActive = true,
            });
        }
    }

    private async Task<SalesOrder> SeedOrderAsync(params SalesOrderLine[] lines)
    {
        _db.Customers.Add(new Customer { Id = 1, Name = "Acme" });
        var so = new SalesOrder
        {
            Id = 501, OrderNumber = "SO-00001", CustomerId = 1, Status = SalesOrderStatus.Confirmed,
            RequestedDeliveryDate = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero),
        };
        foreach (var line in lines)
            so.Lines.Add(line);
        _db.SalesOrders.Add(so);
        await _db.SaveChangesAsync();
        return so;
    }

    private Part AddPart(int id, string partNumber, ProcurementSource source, bool withRouting = false, int? bomRevisionId = null)
    {
        var part = new Part
        {
            Id = id, PartNumber = partNumber, Name = partNumber, ProcurementSource = source,
            CurrentBomRevisionId = bomRevisionId,
        };
        _db.Parts.Add(part);
        if (withRouting)
            _db.Operations.Add(new Operation { PartId = id, StepNumber = 10, Title = "Mill" });
        return part;
    }

    private static SalesOrderLine Line(int id, int lineNumber, int? partId, decimal quantity, string description = "Line") =>
        new() { Id = id, LineNumber = lineNumber, PartId = partId, Quantity = quantity, UnitPrice = 5m, Description = description };

    [Fact]
    public async Task Job_starts_at_order_confirmed_stage()
    {
        SeedTrack();
        AddPart(900, "CW-1001", ProcurementSource.Make);
        var so = await SeedOrderAsync(Line(601, 1, 900, 2m));

        await Handler().Handle(new CreateJobsForSalesOrderLinesCommand(so.Id), CancellationToken.None);

        var job = await _db.Jobs.SingleAsync();
        job.CurrentStageId.Should().Be(73,
            "a job born from a confirmed order belongs at order_confirmed, the SO surface's entry point — " +
            "starting lower makes the confirmed SO invisible on the Sales Orders list");
        job.SalesOrderLineId.Should().Be(601);
    }

    [Fact]
    public async Task Track_without_order_confirmed_stage_falls_back_to_first_active_stage()
    {
        SeedTrack(withOrderConfirmedStage: false);
        AddPart(900, "CW-1001", ProcurementSource.Make);
        var so = await SeedOrderAsync(Line(601, 1, 900, 2m));

        await Handler().Handle(new CreateJobsForSalesOrderLinesCommand(so.Id), CancellationToken.None);

        (await _db.Jobs.SingleAsync()).CurrentStageId.Should().Be(70);
    }

    [Fact]
    public async Task Freight_line_is_skipped_and_part_lines_get_jobs_titled_by_part_and_quantity()
    {
        SeedTrack();
        AddPart(900, "CW-1001", ProcurementSource.Make);
        AddPart(901, "40-1700M", ProcurementSource.Make);
        var so = await SeedOrderAsync(
            Line(601, 1, 900, 50m),
            Line(602, 2, null, 1m, "Freight"),
            Line(603, 3, 901, 250m));

        var result = await Handler().Handle(new CreateJobsForSalesOrderLinesCommand(so.Id), CancellationToken.None);

        result.Created.Should().Be(2);
        result.Skipped.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { LineNumber = 2, Reason = "No part on this line" });
        var jobs = await _db.Jobs.OrderBy(j => j.SalesOrderLineId).ToListAsync();
        jobs.Select(j => j.Title).Should().Equal("CW-1001 x 50", "40-1700M x 250");

        var activity = await _db.ActivityLogs.SingleAsync(a => a.Action == "jobs_auto_created");
        activity.EntityType.Should().Be("SalesOrder");
        activity.EntityId.Should().Be(so.Id);
        activity.Description.Should().Contain("2 job(s)").And.Contain("line 2 (no part on this line)");
    }

    [Fact]
    public async Task Job_pins_bom_revision_records_quantity_and_takes_the_requested_delivery_date()
    {
        SeedTrack();
        AddPart(900, "CW-1001", ProcurementSource.Make, bomRevisionId: 44);
        var so = await SeedOrderAsync(Line(601, 1, 900, 50m));

        await Handler().Handle(new CreateJobsForSalesOrderLinesCommand(so.Id), CancellationToken.None);

        var job = await _db.Jobs.Include(j => j.JobParts).SingleAsync();
        job.BomRevisionIdAtRelease.Should().Be(44);
        job.DueDate.Should().Be(so.RequestedDeliveryDate);
        var jobPart = job.JobParts.Should().ContainSingle().Subject;
        jobPart.PartId.Should().Be(900);
        jobPart.Quantity.Should().Be(50m,
            "quantity has to be queryable to compute allocation against the ordered quantity — " +
            "a number in the description string cannot be summed");
        _identifiers.Verify(i => i.IssueAsync(BusinessEntityType.Job, job.Id, job.JobNumber, It.IsAny<CancellationToken>()), Times.Once);
        _barcodes.Verify(b => b.CreateBarcodeAsync(BarcodeEntityType.Job, job.Id, job.JobNumber, It.IsAny<CancellationToken>()), Times.Once);
        _boardClients.Verify(c => c.SendCoreAsync("jobCreated", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Buy_part_with_a_routing_gets_a_job_but_buy_and_phantom_parts_without_one_are_skipped()
    {
        SeedTrack();
        AddPart(900, "RTD-1", ProcurementSource.Buy, withRouting: true);
        AddPart(901, "BOLT-1", ProcurementSource.Buy);
        AddPart(902, "KIT-1", ProcurementSource.Phantom);
        var so = await SeedOrderAsync(Line(601, 1, 900, 5m), Line(602, 2, 901, 5m), Line(603, 3, 902, 5m));

        var result = await Handler().Handle(new CreateJobsForSalesOrderLinesCommand(so.Id), CancellationToken.None);

        result.Created.Should().Be(1);
        (await _db.Jobs.SingleAsync()).SalesOrderLineId.Should().Be(601);
        result.Skipped.Should().BeEquivalentTo(new[]
        {
            new { LineNumber = 2, Reason = "Bought part with no routing" },
            new { LineNumber = 3, Reason = "Phantom part with no routing" },
        });
    }

    [Fact]
    public async Task Create_missing_creates_only_for_unlinked_lines()
    {
        SeedTrack();
        AddPart(900, "CW-1001", ProcurementSource.Make);
        var so = await SeedOrderAsync(Line(601, 1, 900, 5m), Line(602, 2, 900, 6m), Line(603, 3, 900, 7m));
        _db.Jobs.Add(new Job { JobNumber = "J-1", Title = "Existing", TrackTypeId = 7, CurrentStageId = 73, SalesOrderLineId = 601 });
        _db.Jobs.Add(new Job
        {
            JobNumber = "J-2", Title = "Retracted", TrackTypeId = 7, CurrentStageId = 73, SalesOrderLineId = 602,
            Disposition = JobDisposition.EnteredInError, IsArchived = true,
        });
        await _db.SaveChangesAsync();

        var result = await Handler().Handle(new CreateJobsForSalesOrderLinesCommand(so.Id), CancellationToken.None);

        result.Created.Should().Be(2);
        result.Skipped.Should().BeEmpty("a line that already has a live job is not missing one");
        (await _db.Jobs.CountAsync(j => j.SalesOrderLineId == 601)).Should().Be(1);
        (await _db.Jobs.CountAsync(j => j.SalesOrderLineId == 602 && j.Disposition == null)).Should().Be(1,
            "a job retracted as entered in error no longer covers its line");
        (await _db.Jobs.CountAsync(j => j.SalesOrderLineId == 603)).Should().Be(1);
    }

    [Fact]
    public async Task Requested_line_that_already_has_a_job_is_reported_as_skipped()
    {
        SeedTrack();
        AddPart(900, "CW-1001", ProcurementSource.Make);
        var so = await SeedOrderAsync(Line(601, 1, 900, 5m), Line(602, 2, 900, 6m));
        _db.Jobs.Add(new Job { JobNumber = "J-1", Title = "Existing", TrackTypeId = 7, CurrentStageId = 73, SalesOrderLineId = 601 });
        await _db.SaveChangesAsync();

        var result = await Handler().Handle(new CreateJobsForSalesOrderLinesCommand(so.Id, [601]), CancellationToken.None);

        result.Created.Should().Be(0);
        result.Skipped.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { LineNumber = 1, Reason = "Already has a job" });
        (await _db.Jobs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Missing_production_track_refuses()
    {
        AddPart(900, "CW-1001", ProcurementSource.Make);
        var so = await SeedOrderAsync(Line(601, 1, 900, 5m));

        var act = () => Handler().Handle(new CreateJobsForSalesOrderLinesCommand(so.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("No production track is set up*");
    }

    [Fact]
    public async Task Draft_order_refuses()
    {
        SeedTrack();
        AddPart(900, "CW-1001", ProcurementSource.Make);
        var so = await SeedOrderAsync(Line(601, 1, 900, 5m));
        so.Status = SalesOrderStatus.Draft;
        await _db.SaveChangesAsync();

        var act = () => Handler().Handle(new CreateJobsForSalesOrderLinesCommand(so.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _db.Jobs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Order_without_acceptance_proof_refuses()
    {
        SeedTrack();
        AddPart(900, "CW-1001", ProcurementSource.Make);
        var so = await SeedOrderAsync(Line(601, 1, 900, 5m));
        _acceptanceGate.Setup(g => g.EnsureReleasableAsync(so.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no acceptance"));

        var act = () => Handler().Handle(new CreateJobsForSalesOrderLinesCommand(so.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("no acceptance");
        (await _db.Jobs.CountAsync()).Should().Be(0);
    }
}
