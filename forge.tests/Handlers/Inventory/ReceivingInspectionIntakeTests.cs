using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Features.Inventory;
using Forge.Api.Features.PurchaseOrders;
using Forge.Api.Features.Scanner;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Inventory;

public class ReceivingInspectionIntakeTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeCapabilities(bool inspectionOn) : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal) { [ReceivingInspectionPolicy.Capability] = inspectionOn },
            DateTimeOffset.UtcNow);
        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private readonly FixedClock _clock = new();
    private readonly Mock<IHttpContextAccessor> _httpContextAccessor = new();

    public ReceivingInspectionIntakeTests()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Name, "Receiver")], "Test");
        _httpContextAccessor.Setup(a => a.HttpContext)
            .Returns(new DefaultHttpContext { User = new ClaimsPrincipal(identity) });
    }

    public static TheoryData<bool, bool, ReceivingInspectionStatus> Matrix => new()
    {
        { true, true, ReceivingInspectionStatus.Pending },
        { true, false, ReceivingInspectionStatus.NotRequired },
        { false, true, ReceivingInspectionStatus.NotRequired },
        { false, false, ReceivingInspectionStatus.NotRequired },
    };

    private static PurchaseOrderLine Line(bool partRequiresInspection)
    {
        var part = new Part { Id = 5, PartNumber = "P-5", Description = "Bracket", RequiresReceivingInspection = partRequiresInspection };
        var po = new PurchaseOrder { Id = 100, PONumber = "PO-100", VendorId = 1, Status = PurchaseOrderStatus.Acknowledged };
        var line = new PurchaseOrderLine
        {
            Id = 1,
            PurchaseOrderId = po.Id,
            PurchaseOrder = po,
            PartId = part.Id,
            Part = part,
            OrderedQuantity = 10m,
            UnitPrice = 5m,
            Description = "Bracket",
        };
        po.Lines.Add(line);
        return line;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task ReceiveItems_SetsInspectionStatusFromPartAndCapability(
        bool partRequiresInspection, bool capabilityOn, ReceivingInspectionStatus expected)
    {
        var line = Line(partRequiresInspection);
        var added = new List<ReceivingRecord>();
        var repo = new Mock<IPurchaseOrderRepository>();
        repo.Setup(r => r.FindWithDetailsAsync(line.PurchaseOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(line.PurchaseOrder);
        repo.Setup(r => r.AddReceivingRecordAsync(It.IsAny<ReceivingRecord>(), It.IsAny<CancellationToken>()))
            .Callback<ReceivingRecord, CancellationToken>((r, _) => added.Add(r))
            .Returns(Task.CompletedTask);
        var handler = new ReceiveItemsHandler(
            repo.Object, _clock, new Mock<MediatR.IMediator>().Object, _httpContextAccessor.Object,
            capabilities: new FakeCapabilities(capabilityOn));

        await handler.Handle(new ReceiveItemsCommand(
            line.PurchaseOrderId,
            [new ReceiveLineModel(LineId: line.Id, Quantity: 10m, StorageLocationId: null, Notes: null)]),
            CancellationToken.None);

        added.Should().ContainSingle().Which.InspectionStatus.Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task ReceivePurchaseOrder_SetsInspectionStatusFromPartAndCapability(
        bool partRequiresInspection, bool capabilityOn, ReceivingInspectionStatus expected)
    {
        var line = Line(partRequiresInspection);
        var added = new List<ReceivingRecord>();
        var repo = new Mock<IPurchaseOrderRepository>();
        repo.Setup(r => r.FindLineAsync(line.Id, It.IsAny<CancellationToken>())).ReturnsAsync(line);
        repo.Setup(r => r.AddReceivingRecordAsync(It.IsAny<ReceivingRecord>(), It.IsAny<CancellationToken>()))
            .Callback<ReceivingRecord, CancellationToken>((r, _) => added.Add(r))
            .Returns(Task.CompletedTask);
        var handler = new ReceivePurchaseOrderHandler(
            repo.Object, new Mock<IInventoryRepository>().Object, _httpContextAccessor.Object, _clock,
            capabilities: new FakeCapabilities(capabilityOn));

        await handler.Handle(
            new ReceivePurchaseOrderCommand(new ReceivePurchaseOrderRequestModel(line.Id, 10m, null, null, null)),
            CancellationToken.None);

        added.Should().ContainSingle().Which.InspectionStatus.Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task ScanReceive_SetsInspectionStatusFromPartAndCapability(
        bool partRequiresInspection, bool capabilityOn, ReceivingInspectionStatus expected)
    {
        await using var db = TestDbContextFactory.Create();
        var vendor = new Vendor { CompanyName = "Supplier" };
        var part = new Part { PartNumber = $"P-{Guid.NewGuid():N}", Description = "Bracket", RequiresReceivingInspection = partRequiresInspection };
        var bin = new StorageLocation { Name = "Dock", LocationType = LocationType.Bin };
        db.AddRange(vendor, part, bin);
        await db.SaveChangesAsync();
        var po = new PurchaseOrder { PONumber = "PO-200", VendorId = vendor.Id, Status = PurchaseOrderStatus.Submitted };
        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync();
        var line = new PurchaseOrderLine
        {
            PurchaseOrderId = po.Id,
            PartId = part.Id,
            Description = "Bracket",
            OrderedQuantity = 10m,
            UnitPrice = 5m,
        };
        db.PurchaseOrderLines.Add(line);
        await db.SaveChangesAsync();

        var handler = new ExecuteScanReceiveHandler(db, _clock, _httpContextAccessor.Object, new FakeCapabilities(capabilityOn));

        await handler.Handle(
            new ExecuteScanReceiveCommand(new ScanReceiveRequestModel(part.Id, line.Id, 10m, bin.Id)),
            CancellationToken.None);

        (await db.ReceivingRecords.SingleAsync()).InspectionStatus.Should().Be(expected);
    }
}
