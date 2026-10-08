using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.Replenishment;
using Forge.Api.Features.SalesOrders.Acceptance;
using Forge.Api.Hubs;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Replenishment;

public class ApproveMakeSuggestionTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => FixedNow;
    }

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IJobRepository> _jobRepo = new();
    private int _jobNumber = 100;

    public ApproveMakeSuggestionTests()
    {
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        var boardHub = new Mock<IHubContext<BoardHub>>();
        boardHub.Setup(h => h.Clients).Returns(clients.Object);

        _jobRepo.Setup(r => r.GenerateNextJobNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"J-{++_jobNumber}");
        _jobRepo.Setup(r => r.GetMaxBoardPositionAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _jobRepo.Setup(r => r.AddAsync(It.IsAny<Job>(), It.IsAny<CancellationToken>()))
            .Callback<Job, CancellationToken>((job, _) => _db.Jobs.Add(job))
            .Returns(Task.CompletedTask);
        _jobRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(ct => _db.SaveChangesAsync(ct));

        var createJob = new CreateJobHandler(
            _jobRepo.Object, new TrackTypeRepository(_db), _mediator.Object, boardHub.Object,
            Mock.Of<IBarcodeService>(),
            Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
            _db,
            new SalesOrderAcceptanceGate(_db, StubCapabilitySnapshotProvider.Off),
            Mock.Of<ICloudFolderAutoCreator>(),
            Mock.Of<ISystemSettingRepository>(),
            Mock.Of<IBusinessIdentifierService>(),
            StubCapabilitySnapshotProvider.Off);
        var getSuggestions = new GetReorderSuggestionsHandler(_db, new PartSourcingResolver(_db));

        _mediator.Setup(m => m.Send(It.IsAny<CreateJobCommand>(), It.IsAny<CancellationToken>()))
            .Returns<CreateJobCommand, CancellationToken>((cmd, ct) => createJob.Handle(cmd, ct));
        _mediator.Setup(m => m.Send(It.IsAny<GetReorderSuggestionsQuery>(), It.IsAny<CancellationToken>()))
            .Returns<GetReorderSuggestionsQuery, CancellationToken>((q, ct) => getSuggestions.Handle(q, ct));
        _mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .Returns<GetJobByIdQuery, CancellationToken>((q, _) => Task.FromResult(JobDetail(q.Id)));
    }

    private static JobDetailResponseModel JobDetail(int id) => new(
        id, "J", "Test", null, 1, "Production",
        1, "Confirmed", "#94a3b8", null, null, null, null,
        "Normal", null, null, null, null, null, false, 1, 0, null,
        null, null, null, null, null, null, null, null, null, null, 0,
        FixedNow, FixedNow);

    private ApproveSuggestionHandler ApproveHandler() => new(
        _db, new PurchaseOrderRepository(_db), Mock.Of<IBarcodeService>(),
        new PartSourcingResolver(_db), _mediator.Object, new FixedClock());

    private ApproveBulkSuggestionsHandler BulkHandler() => new(
        _db, new PurchaseOrderRepository(_db), Mock.Of<IBarcodeService>(),
        new PartSourcingResolver(_db), _mediator.Object, new FixedClock());

    private async Task SeedTrackAsync()
    {
        var track = new TrackType { Code = "production", Name = "Production", IsDefault = true, SortOrder = 1 };
        track.Stages.Add(new JobStage { Code = "order_confirmed", Name = "Confirmed", SortOrder = 1 });
        _db.TrackTypes.Add(track);
        await _db.SaveChangesAsync();
    }

    private async Task<ReorderSuggestion> SeedSuggestionAsync(
        string partNumber, ProcurementSource source, decimal quantity, int? preferredVendorId = null)
    {
        var part = new Part
        {
            PartNumber = partNumber,
            Name = partNumber,
            ProcurementSource = source,
            InventoryClass = InventoryClass.Component,
            Status = PartStatus.Active,
            PreferredVendorId = preferredVendorId,
        };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();

        if (source == ProcurementSource.Make)
            _db.Operations.Add(new Operation { PartId = part.Id, StepNumber = 10, Title = "Mold", RunMinutesEach = 48m });

        var suggestion = new ReorderSuggestion
        {
            PartId = part.Id,
            VendorId = source == ProcurementSource.Make ? null : preferredVendorId,
            SuggestedQuantity = quantity,
        };
        _db.ReorderSuggestions.Add(suggestion);
        await _db.SaveChangesAsync();

        _db.FollowUpTasks.Add(new FollowUpTask
        {
            Title = "To do",
            AssignedToUserId = 1,
            SourceEntityType = ReplenishmentAssignee.TaskSourceEntityType,
            SourceEntityId = suggestion.Id,
            TriggerType = FollowUpTriggerType.ReorderSuggested,
        });
        await _db.SaveChangesAsync();
        return suggestion;
    }

    private Task<FollowUpTask> TaskFor(int suggestionId) =>
        _db.FollowUpTasks.SingleAsync(t => t.SourceEntityId == suggestionId);

    [Fact]
    public async Task Approving_a_make_suggestion_creates_a_linked_work_order_and_completes_the_task()
    {
        await SeedTrackAsync();
        var suggestion = await SeedSuggestionAsync("MAKE-200", ProcurementSource.Make, 100m);

        var result = await ApproveHandler().Handle(new ApproveSuggestionCommand(suggestion.Id, 7), default);

        var job = await _db.Jobs.Include(j => j.JobParts).SingleAsync();
        job.Title.Should().Be("MAKE-200 x 100");
        job.PartId.Should().Be(suggestion.PartId);
        job.Priority.Should().Be(JobPriority.Normal);
        job.DueDate.Should().Be(FixedNow.AddDays(10));
        job.JobParts.Should().ContainSingle()
            .Which.Should().Match<JobPart>(jp => jp.PartId == suggestion.PartId && jp.Quantity == 100m);

        suggestion.ResultingJobId.Should().Be(job.Id);
        suggestion.ResultingPurchaseOrderId.Should().BeNull();
        suggestion.Status.Should().Be(ReorderSuggestionStatus.Approved);
        suggestion.ApprovedAt.Should().Be(FixedNow);
        (await _db.PurchaseOrders.AnyAsync()).Should().BeFalse();

        result.SupplyType.Should().Be("Make");
        result.ResultingJobId.Should().Be(job.Id);
        result.ResultingJobNumber.Should().Be(job.JobNumber);
        result.LeadTimeDays.Should().Be(10);

        var task = await TaskFor(suggestion.Id);
        task.Status.Should().Be(FollowUpStatus.Completed);
        task.CompletedAt.Should().Be(FixedNow);
    }

    [Fact]
    public async Task Approving_a_make_suggestion_without_a_production_track_fails()
    {
        var suggestion = await SeedSuggestionAsync("MAKE-NOTRACK", ProcurementSource.Make, 5m);

        var act = () => ApproveHandler().Handle(new ApproveSuggestionCommand(suggestion.Id, 7), default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        suggestion.Status.Should().Be(ReorderSuggestionStatus.Pending);
    }

    [Fact]
    public async Task Approving_a_buy_suggestion_uses_the_resolved_vendor_and_logs_the_po()
    {
        var snapshotVendor = new Vendor { CompanyName = "Snapshot Vendor" };
        var preferredVendor = new Vendor { CompanyName = "Preferred Vendor" };
        _db.Vendors.AddRange(snapshotVendor, preferredVendor);
        await _db.SaveChangesAsync();
        var suggestion = await SeedSuggestionAsync("BUY-200", ProcurementSource.Buy, 12.5m, snapshotVendor.Id);
        _db.VendorParts.Add(new VendorPart { VendorId = preferredVendor.Id, PartId = suggestion.PartId, IsPreferred = true });
        await _db.SaveChangesAsync();

        var result = await ApproveHandler().Handle(new ApproveSuggestionCommand(suggestion.Id, 7), default);

        var po = await _db.PurchaseOrders.Include(p => p.Lines).SingleAsync();
        po.VendorId.Should().Be(preferredVendor.Id);
        po.Lines.Should().ContainSingle().Which.OrderedQuantity.Should().Be(13m);
        result.SupplyType.Should().Be("Buy");
        result.VendorName.Should().Be("Preferred Vendor");
        result.ResultingPurchaseOrderId.Should().Be(po.Id);
        (await _db.ActivityLogs.SingleAsync(a => a.EntityType == "PurchaseOrder" && a.EntityId == po.Id && a.Action == "created"))
            .Description.Should().Contain(po.PONumber).And.Contain("Preferred Vendor");
        (await TaskFor(suggestion.Id)).Status.Should().Be(FollowUpStatus.Completed);
    }

    [Fact]
    public async Task Bulk_approve_makes_one_job_per_make_suggestion_and_groups_buys_by_vendor()
    {
        await SeedTrackAsync();
        var vendor = new Vendor { CompanyName = "Steel Supply" };
        _db.Vendors.Add(vendor);
        await _db.SaveChangesAsync();

        var makeA = await SeedSuggestionAsync("MAKE-A", ProcurementSource.Make, 10m);
        var makeB = await SeedSuggestionAsync("MAKE-B", ProcurementSource.Make, 20m);
        var buyA = await SeedSuggestionAsync("BUY-A", ProcurementSource.Buy, 5m, vendor.Id);
        var buyB = await SeedSuggestionAsync("BUY-B", ProcurementSource.Subcontract, 6m, vendor.Id);
        var noVendor = await SeedSuggestionAsync("BUY-NOVENDOR", ProcurementSource.Buy, 7m);

        var result = await BulkHandler().Handle(
            new ApproveBulkSuggestionsCommand([makeA.Id, makeB.Id, buyA.Id, buyB.Id, noVendor.Id], 7), default);

        result.ApprovedCount.Should().Be(4);
        result.SkippedCount.Should().Be(1);
        result.CreatedJobIds.Should().HaveCount(2);
        result.CreatedPoIds.Should().ContainSingle();

        var jobs = await _db.Jobs.Include(j => j.JobParts).ToListAsync();
        jobs.Should().HaveCount(2);
        makeA.ResultingJobId.Should().NotBeNull();
        makeB.ResultingJobId.Should().NotBeNull().And.NotBe(makeA.ResultingJobId);
        jobs.Single(j => j.Id == makeB.ResultingJobId).JobParts.Single().Quantity.Should().Be(20m);

        var po = await _db.PurchaseOrders.Include(p => p.Lines).SingleAsync();
        po.Lines.Should().HaveCount(2);
        buyA.ResultingPurchaseOrderId.Should().Be(po.Id);
        buyB.ResultingPurchaseOrderId.Should().Be(po.Id);
        noVendor.Status.Should().Be(ReorderSuggestionStatus.Pending);

        (await TaskFor(makeA.Id)).Status.Should().Be(FollowUpStatus.Completed);
        (await TaskFor(buyB.Id)).Status.Should().Be(FollowUpStatus.Completed);
        (await TaskFor(noVendor.Id)).Status.Should().Be(FollowUpStatus.Open);
    }

    [Fact]
    public async Task Dismissing_a_suggestion_dismisses_its_task()
    {
        var suggestion = await SeedSuggestionAsync("MAKE-DISMISS", ProcurementSource.Make, 5m);

        await new DismissSuggestionHandler(_db, new FixedClock())
            .Handle(new DismissSuggestionCommand(suggestion.Id, 7, "Not needed"), default);

        suggestion.Status.Should().Be(ReorderSuggestionStatus.Dismissed);
        suggestion.DismissedAt.Should().Be(FixedNow);
        var task = await TaskFor(suggestion.Id);
        task.Status.Should().Be(FollowUpStatus.Dismissed);
        task.DismissedAt.Should().Be(FixedNow);
    }
}
