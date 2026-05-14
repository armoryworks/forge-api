using FluentAssertions;
using Moq;

using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Integrations;

namespace Forge.Tests.Handlers.PurchaseOrders;

/// <summary>
/// Bought-parts effort PR3 — ReceiveItems handler tests cover the four
/// allocation methods plus the freight-defaulting behavior.
/// </summary>
public class ReceiveItemsHandlerTests
{
    private readonly Mock<IPurchaseOrderRepository> _repo = new();
    private readonly Mock<IPurchaseOrderRepository> _capturedRepo;
    private readonly Mock<MediatR.IMediator> _mediator = new();
    private readonly Mock<Microsoft.AspNetCore.Http.IHttpContextAccessor> _httpContext = new();
    private readonly IClock _clock = new SystemClock();
    private readonly ReceiveItemsHandler _handler;

    private readonly List<ReceivingRecord> _addedRecords = new();

    public ReceiveItemsHandlerTests()
    {
        _capturedRepo = _repo;
        _capturedRepo.Setup(r => r.AddReceivingRecordAsync(It.IsAny<ReceivingRecord>(), It.IsAny<CancellationToken>()))
            .Callback<ReceivingRecord, CancellationToken>((r, _) => _addedRecords.Add(r))
            .Returns(Task.CompletedTask);

        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ctx.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "1") }));
        _httpContext.Setup(x => x.HttpContext).Returns(ctx);

        _handler = new ReceiveItemsHandler(_repo.Object, _clock, _mediator.Object, _httpContext.Object);
    }

    private static PurchaseOrder PoWith(decimal? estimatedFreight, params (int lineId, int partId, decimal qty, decimal unitPrice)[] lines)
    {
        var po = new PurchaseOrder
        {
            Id = 100,
            PONumber = "PO-100",
            VendorId = 1,
            EstimatedFreight = estimatedFreight,
            Status = PurchaseOrderStatus.Acknowledged,
        };
        foreach (var (lineId, partId, qty, price) in lines)
        {
            po.Lines.Add(new PurchaseOrderLine
            {
                Id = lineId,
                PartId = partId,
                OrderedQuantity = qty,
                ReceivedQuantity = 0,
                UnitPrice = price,
                Description = $"Part {partId}",
            });
        }
        return po;
    }

    [Fact]
    public async Task Handle_ByExtendedValue_AllocatesProportionally()
    {
        // 100 freight, two lines: $200 and $600 extended → 25/75 split.
        var po = PoWith(estimatedFreight: null,
            (1, 10, qty: 10m, unitPrice: 20m),  // $200 extended
            (2, 11, qty: 6m, unitPrice: 100m)); // $600 extended
        _repo.Setup(r => r.FindWithDetailsAsync(po.Id, It.IsAny<CancellationToken>())).ReturnsAsync(po);

        await _handler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel>
            {
                new(LineId: 1, Quantity: 10m, StorageLocationId: null, Notes: null),
                new(LineId: 2, Quantity: 6m, StorageLocationId: null, Notes: null),
            },
            ActualFreight: 100m,
            FreightAllocationMethod: FreightAllocationMethod.ByExtendedValue), CancellationToken.None);

        _addedRecords.Should().HaveCount(2);
        _addedRecords[0].AllocatedFreight.Should().Be(25m);
        _addedRecords[1].AllocatedFreight.Should().Be(75m);
        _addedRecords.Should().AllSatisfy(r => r.ReceiptNumber.Should().NotBeNullOrEmpty());
        _addedRecords.Select(r => r.ReceiptNumber).Distinct().Should().HaveCount(1, "all records in one call share a receipt number");
    }

    [Fact]
    public async Task Handle_ByQuantity_AllocatesPerUnitEvenly()
    {
        // 90 freight, two lines: 10 + 5 units = 15 total → 60/30 split.
        var po = PoWith(estimatedFreight: null,
            (1, 10, qty: 10m, unitPrice: 5m),
            (2, 11, qty: 5m, unitPrice: 200m));
        _repo.Setup(r => r.FindWithDetailsAsync(po.Id, It.IsAny<CancellationToken>())).ReturnsAsync(po);

        await _handler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel>
            {
                new(LineId: 1, Quantity: 10m, StorageLocationId: null, Notes: null),
                new(LineId: 2, Quantity: 5m, StorageLocationId: null, Notes: null),
            },
            ActualFreight: 90m,
            FreightAllocationMethod: FreightAllocationMethod.ByQuantity), CancellationToken.None);

        _addedRecords[0].AllocatedFreight.Should().Be(60m);
        _addedRecords[1].AllocatedFreight.Should().Be(30m);
    }

    [Fact]
    public async Task Handle_Manual_UsesCallerSuppliedPerLineFreight()
    {
        var po = PoWith(estimatedFreight: null,
            (1, 10, qty: 5m, unitPrice: 10m),
            (2, 11, qty: 5m, unitPrice: 10m));
        _repo.Setup(r => r.FindWithDetailsAsync(po.Id, It.IsAny<CancellationToken>())).ReturnsAsync(po);

        await _handler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel>
            {
                new(LineId: 1, Quantity: 5m, StorageLocationId: null, Notes: null, ManualFreight: 70m),
                new(LineId: 2, Quantity: 5m, StorageLocationId: null, Notes: null, ManualFreight: 30m),
            },
            ActualFreight: 100m,
            FreightAllocationMethod: FreightAllocationMethod.Manual), CancellationToken.None);

        _addedRecords[0].AllocatedFreight.Should().Be(70m);
        _addedRecords[1].AllocatedFreight.Should().Be(30m);
    }

    [Fact]
    public async Task Handle_NullActualFreight_DefaultsFromPoEstimate()
    {
        // PO has $50 estimated freight; caller doesn't pass an actual.
        // Records should record ActualFreight = $50 and allocate it.
        var po = PoWith(estimatedFreight: 50m,
            (1, 10, qty: 1m, unitPrice: 200m));
        _repo.Setup(r => r.FindWithDetailsAsync(po.Id, It.IsAny<CancellationToken>())).ReturnsAsync(po);

        await _handler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel> { new(LineId: 1, Quantity: 1m, StorageLocationId: null, Notes: null) }),
            CancellationToken.None);

        _addedRecords.Single().ActualFreight.Should().Be(50m);
        _addedRecords.Single().AllocatedFreight.Should().Be(50m);
    }

    [Fact]
    public async Task Handle_NullFreightEverywhere_LeavesAllocationNull()
    {
        // No PO estimate, no caller actual → record captures the line but
        // skips allocation. Pre-PR3 records also fall here.
        var po = PoWith(estimatedFreight: null,
            (1, 10, qty: 2m, unitPrice: 5m));
        _repo.Setup(r => r.FindWithDetailsAsync(po.Id, It.IsAny<CancellationToken>())).ReturnsAsync(po);

        await _handler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel> { new(LineId: 1, Quantity: 2m, StorageLocationId: null, Notes: null) }),
            CancellationToken.None);

        _addedRecords.Single().ActualFreight.Should().BeNull();
        _addedRecords.Single().AllocatedFreight.Should().BeNull();
    }
}
