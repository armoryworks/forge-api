using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.PurchaseOrders;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class SendOutSubcontractPoTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 14, 30, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IClock> _clock = new();
    private readonly SendOutSubcontractHandler _handler;

    public SendOutSubcontractPoTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);

        var poHandler = new CreatePurchaseOrderHandler(
            new PurchaseOrderRepository(_db),
            new VendorRepository(_db),
            new PartRepository(_db, Mock.Of<IPartPricingResolver>()),
            Mock.Of<IBarcodeService>(),
            Mock.Of<ISystemSettingRepository>(),
            Mock.Of<IBusinessIdentifierService>(),
            Mock.Of<IMediator>(),
            Mock.Of<IHttpContextAccessor>(),
            _db,
            _clock.Object);

        _mediator
            .Setup(m => m.Send(It.IsAny<CreatePurchaseOrderCommand>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<PurchaseOrderListItemModel> command, CancellationToken ct) =>
                poHandler.Handle((CreatePurchaseOrderCommand)command, ct));

        _handler = new SendOutSubcontractHandler(_db, _mediator.Object, _clock.Object);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task SendOut_WithoutPurchaseOrder_RecordsSentOrderAndJobActivity()
    {
        var (job, operation, vendor) = await SeedAsync(subcontractCost: 4.25m);
        var expectedBack = Now.AddDays(5);

        var result = await _handler.Handle(
            new SendOutSubcontractCommand(job.Id, operation.Id,
                new SendOutRequestModel(25m, 0m, expectedBack, " 1Z999 ", null)),
            CancellationToken.None);

        result.Status.Should().Be(nameof(SubcontractStatus.Sent));
        result.PurchaseOrderId.Should().BeNull();
        result.PoNumber.Should().BeNull();
        result.VendorName.Should().Be(vendor.CompanyName);
        result.UnitCost.Should().Be(4.25m);
        result.TotalCost.Should().Be(106.25m);
        result.SentAt.Should().Be(Now);

        var order = await _db.SubcontractOrders.SingleAsync();
        order.PurchaseOrderId.Should().BeNull();
        order.ExpectedReturnDate.Should().Be(expectedBack);
        order.ShippingTrackingNumber.Should().Be("1Z999");

        (await _db.PurchaseOrders.CountAsync()).Should().Be(0);
        _mediator.Verify(m => m.Send(It.IsAny<CreatePurchaseOrderCommand>(), It.IsAny<CancellationToken>()), Times.Never);

        var activity = await _db.JobActivityLogs.SingleAsync();
        activity.JobId.Should().Be(job.Id);
        activity.OperationId.Should().Be(operation.Id);
        activity.FieldName.Should().Be("Subcontract");
        activity.NewValue.Should().Be(nameof(SubcontractStatus.Sent));
        activity.Description.Should().Contain("Sent 25 out to Plating Co for Op 20 Anodize");
    }

    [Fact]
    public async Task SendOut_WithPurchaseOrder_CreatesDraftServicePoLinkedToOrder()
    {
        var (job, operation, vendor) = await SeedAsync(subcontractCost: 3.10m);
        var expectedBack = Now.AddDays(7);

        var result = await _handler.Handle(
            new SendOutSubcontractCommand(job.Id, operation.Id,
                new SendOutRequestModel(40m, 0m, expectedBack, null, null, CreatePurchaseOrder: true)),
            CancellationToken.None);

        var po = await _db.PurchaseOrders.Include(p => p.Lines).SingleAsync();
        po.Status.Should().Be(PurchaseOrderStatus.Draft);
        po.PONumber.Should().Be("PO-00001");
        po.VendorId.Should().Be(vendor.Id);
        po.JobId.Should().Be(job.Id);
        po.ExpectedDeliveryDate.Should().Be(expectedBack);

        var line = po.Lines.Should().ContainSingle().Subject;
        line.PartId.Should().BeNull();
        line.Description.Should().Be("Op 20 Anodize for J-1001");
        line.OrderedQuantity.Should().Be(40m);
        line.UnitPrice.Should().Be(3.10m);

        result.PurchaseOrderId.Should().Be(po.Id);
        result.PoNumber.Should().Be("PO-00001");
        (await _db.SubcontractOrders.SingleAsync()).PurchaseOrderId.Should().Be(po.Id);

        var poActivity = await _db.ActivityLogs
            .Where(a => a.EntityType == "PurchaseOrder" && a.EntityId == po.Id && a.Action == "created")
            .ToListAsync();
        poActivity.Should().Contain(a => a.Description.StartsWith("Created purchase order PO-00001 for Plating Co"));

        (await _db.JobActivityLogs.SingleAsync()).Description.Should().EndWith("on draft PO-00001");
    }

    [Fact]
    public async Task SendOut_WithPurchaseOrder_UsesCallerPriceWhenGiven()
    {
        var (job, operation, _) = await SeedAsync(subcontractCost: 3.10m);

        var result = await _handler.Handle(
            new SendOutSubcontractCommand(job.Id, operation.Id,
                new SendOutRequestModel(10m, 5m, null, null, null, CreatePurchaseOrder: true)),
            CancellationToken.None);

        result.UnitCost.Should().Be(5m);
        (await _db.PurchaseOrderLines.SingleAsync()).UnitPrice.Should().Be(5m);
    }

    [Fact]
    public async Task SendOut_OperationNotSubcontracted_Throws()
    {
        var (job, operation, _) = await SeedAsync(subcontractCost: null);
        operation.IsSubcontract = false;
        await _db.SaveChangesAsync();

        var act = () => _handler.Handle(
            new SendOutSubcontractCommand(job.Id, operation.Id,
                new SendOutRequestModel(1m, 0m, null, null, null, CreatePurchaseOrder: true)),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _db.PurchaseOrders.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ReceiveBack_GoodAndScrap_CompletesOrderAndLogsOnJob()
    {
        var (job, operation, _) = await SeedAsync(subcontractCost: 2m);
        var sent = await _handler.Handle(
            new SendOutSubcontractCommand(job.Id, operation.Id,
                new SendOutRequestModel(10m, 0m, null, null, null)),
            CancellationToken.None);
        _clock.SetupGet(c => c.UtcNow).Returns(Now.AddDays(3));

        var receiver = new ReceiveBackSubcontractHandler(_db, new JobOperationService(_db, Mock.Of<ISettingsService>(), _clock.Object), _clock.Object);
        var result = await receiver.Handle(
            new ReceiveBackSubcontractCommand(sent.Id,
                new ReceiveBackRequestModel(8m, null, PassedInspection: true, ScrapQuantity: 2m)),
            CancellationToken.None);

        result.Status.Should().Be(nameof(SubcontractStatus.Complete));
        result.ReceivedQuantity.Should().Be(8m);
        result.ReceivedAt.Should().Be(Now.AddDays(3));

        var activity = await _db.JobActivityLogs.OrderByDescending(a => a.Id).FirstAsync();
        activity.OldValue.Should().Be(nameof(SubcontractStatus.Sent));
        activity.NewValue.Should().Be(nameof(SubcontractStatus.Complete));
        activity.OperationId.Should().Be(operation.Id);
        activity.Description.Should().Be("Received back 8 good, 2 scrap from Plating Co for Op 20 Anodize");
    }

    [Fact]
    public async Task ReceiveBack_AlreadyReceived_Throws()
    {
        var (job, operation, _) = await SeedAsync(subcontractCost: 2m);
        var sent = await _handler.Handle(
            new SendOutSubcontractCommand(job.Id, operation.Id,
                new SendOutRequestModel(10m, 0m, null, null, null)),
            CancellationToken.None);
        var receiver = new ReceiveBackSubcontractHandler(_db, new JobOperationService(_db, Mock.Of<ISettingsService>(), _clock.Object), _clock.Object);
        await receiver.Handle(
            new ReceiveBackSubcontractCommand(sent.Id, new ReceiveBackRequestModel(10m, null)),
            CancellationToken.None);

        var act = () => receiver.Handle(
            new ReceiveBackSubcontractCommand(sent.Id, new ReceiveBackRequestModel(10m, null)),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(0, 10, true)]
    [InlineData(10, 0, true)]
    [InlineData(0, 0, false)]
    [InlineData(-1, 5, false)]
    public void ReceiveBackValidator_RequiresSomeQuantityBack(decimal good, decimal scrap, bool valid)
    {
        var result = new ReceiveBackSubcontractValidator().Validate(
            new ReceiveBackSubcontractCommand(1, new ReceiveBackRequestModel(good, null, ScrapQuantity: scrap)));

        result.IsValid.Should().Be(valid);
    }

    [Fact]
    public async Task SubcontractOperations_ListsOnlySubcontractedStepsWithJobQuantity()
    {
        var (job, operation, vendor) = await SeedAsync(subcontractCost: 1.5m);

        var result = await new GetJobSubcontractOperationsHandler(_db).Handle(
            new GetJobSubcontractOperationsQuery(job.Id), CancellationToken.None);

        var row = result.Should().ContainSingle().Subject;
        row.OperationId.Should().Be(operation.Id);
        row.VendorId.Should().Be(vendor.Id);
        row.VendorName.Should().Be("Plating Co");
        row.SubcontractCost.Should().Be(1.5m);
        row.TurnTimeDays.Should().Be(4m);
        row.JobQuantity.Should().Be(40m);
    }

    private async Task<(Job Job, Operation Operation, Vendor Vendor)> SeedAsync(decimal? subcontractCost)
    {
        var vendor = new Vendor { CompanyName = "Plating Co" };
        var part = new Part { PartNumber = "BRK-100", Name = "Bracket" };
        _db.Vendors.Add(vendor);
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();

        _db.Operations.Add(new Operation { PartId = part.Id, StepNumber = 10, Title = "Machine" });
        var operation = new Operation
        {
            PartId = part.Id,
            StepNumber = 20,
            Title = "Anodize",
            IsSubcontract = true,
            SubcontractVendorId = vendor.Id,
            SubcontractCost = subcontractCost,
            SubcontractLeadTimeDays = 4,
        };
        _db.Operations.Add(operation);

        var job = new Job
        {
            JobNumber = "J-1001",
            Title = "Brackets",
            TrackTypeId = 1,
            CurrentStageId = 1,
            PartId = part.Id,
        };
        job.JobParts.Add(new JobPart { PartId = part.Id, Quantity = 40m });
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        return (job, operation, vendor);
    }
}
