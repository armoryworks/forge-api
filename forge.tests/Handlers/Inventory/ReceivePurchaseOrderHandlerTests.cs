using System.Security.Claims;
using Bogus;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using Forge.Api.Features.Inventory;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Inventory;

public class ReceivePurchaseOrderHandlerTests
{
    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow { get; } = DateTimeOffset.UtcNow; }

    private readonly Mock<IPurchaseOrderRepository> _poRepo = new();
    private readonly Mock<IInventoryRepository> _inventoryRepo = new();
    private readonly Mock<IHttpContextAccessor> _httpContextAccessor = new();
    private readonly ReceivePurchaseOrderHandler _handler;
    private readonly Faker _faker = new();
    private readonly int _userId;

    public ReceivePurchaseOrderHandlerTests()
    {
        _userId = _faker.Random.Int(1, 50);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, _userId.ToString()),
            new(ClaimTypes.Name, "Test User"),
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        _httpContextAccessor.Setup(a => a.HttpContext).Returns(httpContext);

        _handler = new ReceivePurchaseOrderHandler(_poRepo.Object, _inventoryRepo.Object, _httpContextAccessor.Object, new FixedClock());
    }

    [Fact]
    public async Task Handle_ValidReceive_UpdatesLineAndReturnsRecord()
    {
        // Arrange
        var lineId = _faker.Random.Int(1, 100);
        var line = new PurchaseOrderLine
        {
            Id = lineId,
            PartId = 5,
            OrderedQuantity = 100,
            ReceivedQuantity = 20,
            UnitPrice = 10m,
            PurchaseOrder = new PurchaseOrder { PONumber = "PO-001" },
        };

        _poRepo.Setup(r => r.FindLineAsync(lineId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(line);

        var data = new ReceivePurchaseOrderRequestModel(lineId, 30, null, null, "Test receive");
        var command = new ReceivePurchaseOrderCommand(data);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.PurchaseOrderLineId.Should().Be(lineId);
        result.QuantityReceived.Should().Be(30);
        line.ReceivedQuantity.Should().Be(50); // 20 + 30
    }

    [Fact]
    public async Task Handle_ExceedsRemainingQuantity_ThrowsInvalidOperationException()
    {
        var lineId = _faker.Random.Int(1, 100);
        var line = new PurchaseOrderLine
        {
            Id = lineId,
            PartId = 5,
            OrderedQuantity = 100,
            ReceivedQuantity = 95,
            UnitPrice = 10m,
            PurchaseOrder = new PurchaseOrder { PONumber = "PO-002" },
        };

        _poRepo.Setup(r => r.FindLineAsync(lineId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(line);

        var data = new ReceivePurchaseOrderRequestModel(lineId, 10, null, null, null);
        var command = new ReceivePurchaseOrderCommand(data);

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*only*remaining*");
    }

    [Fact]
    public async Task Handle_StampsReceiptNumber_AndPostsInventoryGrniAccrual()
    {
        // Phase-2 STAGE C parity: the inv-tab receive must key a GRNI accrual exactly like
        // the primary receive path — a ReceiptNumber on the record and an inline posting call.
        var lineId = _faker.Random.Int(1, 100);
        var line = new PurchaseOrderLine
        {
            Id = lineId,
            PartId = 5,
            PurchaseOrderId = 42,
            OrderedQuantity = 100,
            ReceivedQuantity = 0,
            UnitPrice = 10m,
            PurchaseOrder = new PurchaseOrder { PONumber = "PO-003" },
        };
        _poRepo.Setup(r => r.FindLineAsync(lineId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(line);
        ReceivingRecord? saved = null;
        _poRepo.Setup(r => r.AddReceivingRecordAsync(It.IsAny<ReceivingRecord>(), It.IsAny<CancellationToken>()))
            .Callback<ReceivingRecord, CancellationToken>((rec, _) => saved = rec)
            .Returns(Task.CompletedTask);
        var posting = new Mock<Forge.Api.Features.Accounting.IReceiptInventoryPostingService>();
        var handler = new ReceivePurchaseOrderHandler(
            _poRepo.Object, _inventoryRepo.Object, _httpContextAccessor.Object, new FixedClock(),
            db: null, receiptPosting: posting.Object);

        var data = new ReceivePurchaseOrderRequestModel(lineId, 30, null, null, null);
        await handler.Handle(new ReceivePurchaseOrderCommand(data), CancellationToken.None);

        saved.Should().NotBeNull();
        saved!.ReceiptNumber.Should().NotBeNullOrWhiteSpace();
        posting.Verify(
            p => p.PostReceiptAsync(42, saved.ReceiptNumber!, It.IsAny<DateOnly>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_LineNotFound_ThrowsKeyNotFoundException()
    {
        _poRepo.Setup(r => r.FindLineAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseOrderLine?)null);

        var data = new ReceivePurchaseOrderRequestModel(99999, 1, null, null, null);
        var command = new ReceivePurchaseOrderCommand(data);

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_LotWithStraySpaces_StoresTheTrimmedLotEverywhere()
    {
        var line = new PurchaseOrderLine
        {
            Id = 7,
            PartId = 5,
            OrderedQuantity = 10,
            UnitPrice = 10m,
            PurchaseOrder = new PurchaseOrder { PONumber = "PO-004" },
        };
        _poRepo.Setup(r => r.FindLineAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(line);
        ReceivingRecord? saved = null;
        _poRepo.Setup(r => r.AddReceivingRecordAsync(It.IsAny<ReceivingRecord>(), It.IsAny<CancellationToken>()))
            .Callback<ReceivingRecord, CancellationToken>((rec, _) => saved = rec)
            .Returns(Task.CompletedTask);
        _inventoryRepo.Setup(r => r.FindLocationAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageLocation { Id = 3, Name = "A1" });
        BinContent? content = null;
        _inventoryRepo.Setup(r => r.AddBinContentAsync(It.IsAny<BinContent>(), It.IsAny<CancellationToken>()))
            .Callback<BinContent, CancellationToken>((c, _) => content = c)
            .Returns(Task.CompletedTask);
        BinMovement? movement = null;
        _inventoryRepo.Setup(r => r.AddMovementAsync(It.IsAny<BinMovement>(), It.IsAny<CancellationToken>()))
            .Callback<BinMovement, CancellationToken>((m, _) => movement = m)
            .Returns(Task.CompletedTask);

        var result = await _handler.Handle(
            new ReceivePurchaseOrderCommand(new ReceivePurchaseOrderRequestModel(7, 2, 3, "  HEAT-A ", null)),
            CancellationToken.None);

        saved!.LotNumber.Should().Be("HEAT-A");
        content!.LotNumber.Should().Be("HEAT-A");
        movement!.LotNumber.Should().Be("HEAT-A");
        result.LotNumber.Should().Be("HEAT-A");
    }

    [Fact]
    public async Task Handle_BlankLot_StoresNoLot()
    {
        var line = new PurchaseOrderLine
        {
            Id = 8,
            PartId = 5,
            OrderedQuantity = 10,
            UnitPrice = 10m,
            PurchaseOrder = new PurchaseOrder { PONumber = "PO-005" },
        };
        _poRepo.Setup(r => r.FindLineAsync(8, It.IsAny<CancellationToken>())).ReturnsAsync(line);
        ReceivingRecord? saved = null;
        _poRepo.Setup(r => r.AddReceivingRecordAsync(It.IsAny<ReceivingRecord>(), It.IsAny<CancellationToken>()))
            .Callback<ReceivingRecord, CancellationToken>((rec, _) => saved = rec)
            .Returns(Task.CompletedTask);

        await _handler.Handle(
            new ReceivePurchaseOrderCommand(new ReceivePurchaseOrderRequestModel(8, 2, null, "   ", null)),
            CancellationToken.None);

        saved!.LotNumber.Should().BeNull();
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void Validator_CapsTheTrimmedLotAt100Characters(int length, bool valid)
    {
        var lot = $"  {new string('L', length)}  ";
        var result = new ReceivePurchaseOrderCommandValidator().Validate(
            new ReceivePurchaseOrderCommand(new ReceivePurchaseOrderRequestModel(1, 1, 3, lot, null)));

        result.IsValid.Should().Be(valid);
    }
}
