using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.DomainEvents.Handlers;
using Forge.Api.Features.Jobs;
using Forge.Api.Features.Jobs.Parts;
using Forge.Api.Features.Lots;
using Forge.Api.Features.Mrp;
using Forge.Api.Features.SalesOrders;
using Forge.Api.Features.SalesOrders.Acceptance;
using Forge.Api.Hubs;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class PartRevisionStampTests
{
    private const int TrackTypeId = 7;
    private const int StageId = 71;
    private const int PartId = 900;
    private const int OtherPartId = 901;

    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private static IHubContext<BoardHub> BoardHub()
    {
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        var hub = new Mock<IHubContext<BoardHub>>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);
        return hub.Object;
    }

    private async Task SeedAsync()
    {
        _db.TrackTypes.Add(new TrackType { Id = TrackTypeId, Name = "Production", Code = "production", IsDefault = true, IsActive = true });
        _db.JobStages.Add(new JobStage
        {
            Id = StageId, TrackTypeId = TrackTypeId, Name = "Order Confirmed", Code = "order_confirmed",
            SortOrder = 1, IsActive = true,
        });
        _db.Parts.AddRange(
            new Part { Id = PartId, PartNumber = "CW-1001", Name = "Clutch weight", Revision = "B", ProcurementSource = ProcurementSource.Make },
            new Part { Id = OtherPartId, PartNumber = "SP-2002", Name = "Spacer", Revision = "D", ProcurementSource = ProcurementSource.Make });
        await _db.SaveChangesAsync();
    }

    private async Task<Job> SeedJobAsync(int? partId, string? partRevision = null)
    {
        var job = new Job
        {
            Id = 1, JobNumber = "J-1", Title = "Clutch weights", TrackTypeId = TrackTypeId, CurrentStageId = StageId,
            PartId = partId, PartRevision = partRevision,
        };
        if (partId is int p)
            job.JobParts.Add(new JobPart { PartId = p, Quantity = 10m });
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    private async Task MovePartToRevisionAsync(int partId, string revision)
    {
        var part = await _db.Parts.SingleAsync(p => p.Id == partId);
        part.Revision = revision;
        await _db.SaveChangesAsync();
    }

    private UpdateJobHandler UpdateHandler()
    {
        var repo = new Mock<IJobRepository>();
        repo.Setup(r => r.FindAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns<int, CancellationToken>(async (id, ct) => await _db.Jobs.FindAsync([id], ct));
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(ct => _db.SaveChangesAsync(ct));
        return new UpdateJobHandler(
            repo.Object,
            Mock.Of<IActivityLogRepository>(),
            Mock.Of<IMediator>(),
            BoardHub(),
            Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
            Mock.Of<ISystemSettingRepository>(),
            Mock.Of<IBusinessIdentifierService>(),
            _db,
            StubCapabilitySnapshotProvider.Off);
    }

    private static UpdateJobCommand ChangePart(int partId) =>
        new(1, null, null, null, null, null, null, null, null, PartId: partId);

    [Fact]
    public async Task A_job_created_for_a_part_records_its_revision()
    {
        await SeedAsync();
        Job? created = null;
        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(r => r.GenerateNextJobNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("J-500");
        jobRepo.Setup(r => r.AddAsync(It.IsAny<Job>(), It.IsAny<CancellationToken>()))
            .Callback<Job, CancellationToken>((job, _) => created = job)
            .Returns(Task.CompletedTask);
        var trackRepo = new Mock<ITrackTypeRepository>();
        trackRepo.Setup(r => r.FindFirstActiveStageAsync(TrackTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobStage { Id = StageId, TrackTypeId = TrackTypeId, Name = "Order Confirmed" });
        var handler = new CreateJobHandler(
            jobRepo.Object, trackRepo.Object, Mock.Of<IMediator>(), BoardHub(),
            Mock.Of<IBarcodeService>(),
            Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
            _db,
            new SalesOrderAcceptanceGate(_db, StubCapabilitySnapshotProvider.Off),
            Mock.Of<ICloudFolderAutoCreator>(),
            Mock.Of<ISystemSettingRepository>(),
            Mock.Of<IBusinessIdentifierService>(),
            StubCapabilitySnapshotProvider.Off);

        await handler.Handle(
            new CreateJobCommand("Clutch weights", null, TrackTypeId, null, null, null, null, PartId: PartId),
            CancellationToken.None);

        created!.PartRevision.Should().Be("B");
    }

    [Fact]
    public async Task Jobs_created_from_sales_order_lines_record_the_part_revision()
    {
        await SeedAsync();
        _db.Customers.Add(new Customer { Id = 1, Name = "Design partner" });
        _db.SalesOrders.Add(new SalesOrder
        {
            Id = 501, OrderNumber = "SO-00001", CustomerId = 1, Status = SalesOrderStatus.Confirmed,
            Lines =
            {
                new SalesOrderLine { Id = 601, LineNumber = 1, PartId = PartId, Quantity = 50m, UnitPrice = 5m, Description = "Clutch weight" },
                new SalesOrderLine { Id = 602, LineNumber = 2, PartId = OtherPartId, Quantity = 20m, UnitPrice = 1m, Description = "Spacer" },
            },
        });
        await _db.SaveChangesAsync();
        var jobRepo = new Mock<IJobRepository>();
        var next = 1;
        jobRepo.Setup(r => r.GenerateNextJobNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => $"J-{next++}");
        var handler = new CreateJobsForSalesOrderLinesHandler(
            _db, jobRepo.Object, Mock.Of<IBarcodeService>(), Mock.Of<IBusinessIdentifierService>(),
            BoardHub(), Mock.Of<ISalesOrderAcceptanceGate>(), Mock.Of<IMediator>(), Mock.Of<ICloudFolderAutoCreator>());

        await handler.Handle(new CreateJobsForSalesOrderLinesCommand(501), CancellationToken.None);

        var stamps = await _db.Jobs.OrderBy(j => j.SalesOrderLineId).Select(j => j.PartRevision).ToListAsync();
        stamps.Should().Equal("B", "D");
    }

    [Fact]
    public async Task A_job_released_from_an_mrp_planned_order_records_the_part_revision()
    {
        await SeedAsync();
        _db.MrpRuns.Add(new MrpRun { Id = 1, RunNumber = "MRP-1", Status = MrpRunStatus.Completed, PlanningHorizonDays = 90 });
        _db.MrpPlannedOrders.Add(new MrpPlannedOrder
        {
            Id = 1, MrpRunId = 1, PartId = PartId, OrderType = MrpOrderType.Manufacture,
            Status = MrpPlannedOrderStatus.Planned, Quantity = 40m,
        });
        await _db.SaveChangesAsync();
        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(r => r.GenerateNextJobNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("J-700");
        var handler = new ReleasePlannedOrderHandler(
            _db, Mock.Of<IBarcodeService>(), Mock.Of<IPurchaseOrderRepository>(), jobRepo.Object,
            Mock.Of<IBusinessIdentifierService>(), Mock.Of<IVendorCostResolver>(), Mock.Of<ICurrencyService>(),
            new PartSourcingResolver(_db));

        var result = await handler.Handle(new ReleasePlannedOrderCommand(1), CancellationToken.None);

        (await _db.Jobs.SingleAsync(j => j.Id == result.CreatedJobId)).PartRevision.Should().Be("B");
    }

    [Fact]
    public async Task Sub_jobs_from_a_bom_explosion_record_the_component_revision()
    {
        await SeedAsync();
        var parent = await SeedJobAsync(PartId, "B");
        _db.BOMLines.Add(new BOMLine
        {
            ParentPartId = PartId, ChildPartId = OtherPartId, Quantity = 2, SourceType = BOMSourceType.Make, SortOrder = 1,
        });
        await _db.SaveChangesAsync();
        var children = new List<Job>();
        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(r => r.GenerateNextJobNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("J-2");
        jobRepo.Setup(r => r.AddAsync(It.IsAny<Job>(), It.IsAny<CancellationToken>()))
            .Callback<Job, CancellationToken>((job, _) => children.Add(job))
            .Returns(Task.CompletedTask);
        var handler = new ExplodeJobBomHandler(_db, jobRepo.Object, Mock.Of<IBarcodeService>(), BoardHub(), new PartSourcingResolver(_db));

        await handler.Handle(new ExplodeJobBomCommand(parent.Id), CancellationToken.None);

        children.Should().ContainSingle().Which.PartRevision.Should().Be("D");
    }

    [Fact]
    public async Task The_job_created_event_stamps_a_job_that_has_no_revision_yet()
    {
        await SeedAsync();
        var job = await SeedJobAsync(PartId);

        await new OnJobCreated_StampPartRevision(_db).Handle(new JobCreatedEvent(job.Id, 17), CancellationToken.None);

        (await _db.Jobs.AsNoTracking().SingleAsync(j => j.Id == job.Id)).PartRevision.Should().Be("B");
        var log = await _db.JobActivityLogs.SingleAsync(l => l.JobId == job.Id && l.FieldName == "PartRevision");
        log.NewValue.Should().Be("B");
        log.UserId.Should().Be(17);
    }

    [Fact]
    public async Task The_job_created_event_leaves_an_existing_stamp_alone()
    {
        await SeedAsync();
        var job = await SeedJobAsync(PartId, "B");
        await MovePartToRevisionAsync(PartId, "C");

        await new OnJobCreated_StampPartRevision(_db).Handle(new JobCreatedEvent(job.Id, 17), CancellationToken.None);

        (await _db.Jobs.AsNoTracking().SingleAsync(j => j.Id == job.Id)).PartRevision.Should().Be("B");
        (await _db.JobActivityLogs.AnyAsync(l => l.FieldName == "PartRevision")).Should().BeFalse();
    }

    [Fact]
    public async Task The_job_created_event_ignores_a_job_without_a_part()
    {
        await SeedAsync();
        var job = await SeedJobAsync(null);

        await new OnJobCreated_StampPartRevision(_db).Handle(new JobCreatedEvent(job.Id, 17), CancellationToken.None);

        (await _db.Jobs.AsNoTracking().SingleAsync(j => j.Id == job.Id)).PartRevision.Should().BeNull();
    }

    [Fact]
    public async Task The_first_part_added_to_a_partless_job_stamps_its_revision()
    {
        await SeedAsync();
        var job = await SeedJobAsync(null);

        await new AddJobPartHandler(_db).Handle(new AddJobPartCommand(job.Id, PartId, 5m), CancellationToken.None);

        job.PartRevision.Should().Be("B");
    }

    [Fact]
    public async Task A_second_part_does_not_restamp_the_job()
    {
        await SeedAsync();
        var job = await SeedJobAsync(PartId, "B");

        await new AddJobPartHandler(_db).Handle(new AddJobPartCommand(job.Id, OtherPartId, 5m), CancellationToken.None);

        job.PartId.Should().Be(PartId);
        job.PartRevision.Should().Be("B");
    }

    [Fact]
    public async Task Changing_the_part_before_work_starts_restamps_the_revision()
    {
        await SeedAsync();
        var job = await SeedJobAsync(PartId, "B");

        await UpdateHandler().Handle(ChangePart(OtherPartId), CancellationToken.None);

        job.PartId.Should().Be(OtherPartId);
        job.PartRevision.Should().Be("D");
    }

    [Fact]
    public async Task Once_work_has_started_the_stamp_cannot_be_overwritten()
    {
        await SeedAsync();
        var job = await SeedJobAsync(PartId, "B");
        _db.TimeEntries.Add(new TimeEntry { JobId = job.Id, UserId = 3, DurationMinutes = 30 });
        await _db.SaveChangesAsync();

        var act = () => UpdateHandler().Handle(ChangePart(OtherPartId), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(UpdateJobHandler.PartLockedMessage);
        (await _db.Jobs.AsNoTracking().SingleAsync(j => j.Id == job.Id)).PartRevision.Should().Be("B");
    }

    [Fact]
    public async Task A_rev_b_work_order_still_reads_rev_b_after_the_part_moves_to_rev_c()
    {
        await SeedAsync();
        var job = await SeedJobAsync(null);
        await new AddJobPartHandler(_db).Handle(new AddJobPartCommand(job.Id, PartId, 5m), CancellationToken.None);

        await MovePartToRevisionAsync(PartId, "C");
        var detail = await new GetJobByIdHandler(new JobRepository(_db, new SystemClock()))
            .Handle(new GetJobByIdQuery(job.Id), CancellationToken.None);

        detail.PartRevision.Should().Be("B");
    }

    [Fact]
    public async Task A_lot_from_a_work_order_carries_the_revision_the_job_was_made_to()
    {
        await SeedAsync();
        var job = await SeedJobAsync(PartId, "B");
        await MovePartToRevisionAsync(PartId, "C");

        var lot = await new CreateLotRecordHandler(_db, Mock.Of<IBarcodeService>(), new SystemClock()).Handle(
            new CreateLotRecordCommand(new CreateLotRecordRequestModel(
                "LOT-REV-1", PartId, job.Id, null, null, 10m, null, null, null)),
            CancellationToken.None);

        lot.PartRevision.Should().Be("B");
        (await _db.LotRecords.AsNoTracking().SingleAsync(l => l.Id == lot.Id)).PartRevision.Should().Be("B");
    }

    [Fact]
    public async Task A_lot_without_a_matching_job_carries_the_part_current_revision()
    {
        await SeedAsync();
        var job = await SeedJobAsync(PartId, "B");

        var lot = await new CreateLotRecordHandler(_db, Mock.Of<IBarcodeService>(), new SystemClock()).Handle(
            new CreateLotRecordCommand(new CreateLotRecordRequestModel(
                "LOT-REV-2", OtherPartId, job.Id, null, null, 4m, null, null, null)),
            CancellationToken.None);

        lot.PartRevision.Should().Be("D");
    }
}
