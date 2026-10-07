using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;

using Forge.Api.Features.Inventory;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Inventory;

public class SetOnHandQuantityHandlerTests
{
    private readonly Mock<IInventoryRepository> _repo = new();
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private readonly Mock<IClock> _clock = new();
    private readonly SetOnHandQuantityHandler _handler;

    public SetOnHandQuantityHandlerTests()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "7") }, "Test"));
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext { User = principal });
        _clock.Setup(c => c.UtcNow).Returns(Now);
        _repo.Setup(r => r.FindLocationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageLocation { Id = 5, Name = "A1" });
        _handler = new SetOnHandQuantityHandler(_repo.Object, accessor.Object, _clock.Object);
    }

    private static SetOnHandQuantityCommand Cmd(decimal qty, string reason = "Opening stock", int? po = null, int? vendor = null)
        => new(new SetOnHandQuantityRequestModel(PartId: 3, LocationId: 5, Quantity: qty, Reason: reason,
            Notes: null, SourcePurchaseOrderId: po, VendorId: vendor));

    [Fact]
    public async Task NoExistingContent_createsBinContentAndPositiveMovementCarryingReasonAndProvenance()
    {
        _repo.Setup(r => r.GetActiveBinContentsByPartLocationAsync(3, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BinContent>());
        BinContent? added = null;
        BinMovement? movement = null;
        _repo.Setup(r => r.AddBinContentAsync(It.IsAny<BinContent>(), It.IsAny<CancellationToken>()))
            .Callback<BinContent, CancellationToken>((c, _) => added = c).Returns(Task.CompletedTask);
        _repo.Setup(r => r.AddMovementAsync(It.IsAny<BinMovement>(), It.IsAny<CancellationToken>()))
            .Callback<BinMovement, CancellationToken>((m, _) => movement = m).Returns(Task.CompletedTask);

        await _handler.Handle(Cmd(25, "Opening stock", po: 12, vendor: 4), CancellationToken.None);

        added.Should().NotBeNull();
        added!.EntityId.Should().Be(3);
        added.LocationId.Should().Be(5);
        added.Quantity.Should().Be(25);
        added.PlacedBy.Should().Be(7);
        movement.Should().NotBeNull();
        movement!.Quantity.Should().Be(25);
        movement.ToLocationId.Should().Be(5);
        movement.FromLocationId.Should().BeNull();
        movement.Reason.Should().Be(BinMovementReason.Adjustment);
        movement.Notes.Should().Contain("Opening stock").And.Contain("PO #12").And.Contain("Vendor #4");
    }

    [Fact]
    public async Task ExistingContent_adjustsQuantityAndRecordsDeltaMovementWithoutCreating()
    {
        var existing = new BinContent { Id = 9, EntityType = "part", EntityId = 3, LocationId = 5, Quantity = 5, ReservedQuantity = 0 };
        _repo.Setup(r => r.GetActiveBinContentsByPartLocationAsync(3, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BinContent> { existing });
        BinMovement? movement = null;
        _repo.Setup(r => r.AddMovementAsync(It.IsAny<BinMovement>(), It.IsAny<CancellationToken>()))
            .Callback<BinMovement, CancellationToken>((m, _) => movement = m).Returns(Task.CompletedTask);

        await _handler.Handle(Cmd(12, "Cycle count correction"), CancellationToken.None);

        existing.Quantity.Should().Be(12);
        movement!.Quantity.Should().Be(7);
        movement.ToLocationId.Should().Be(5);
        _repo.Verify(r => r.AddBinContentAsync(It.IsAny<BinContent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact] // Single-location mode: no location supplied -> uses (or creates) the default location.
    public async Task NoLocationSupplied_usesDefaultLocation()
    {
        _repo.Setup(r => r.EnsureDefaultLocationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageLocation { Id = 1, Name = "Main", IsDefault = true });
        _repo.Setup(r => r.GetActiveBinContentsByPartLocationAsync(3, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BinContent>());
        BinContent? added = null;
        _repo.Setup(r => r.AddBinContentAsync(It.IsAny<BinContent>(), It.IsAny<CancellationToken>()))
            .Callback<BinContent, CancellationToken>((c, _) => added = c).Returns(Task.CompletedTask);
        _repo.Setup(r => r.AddMovementAsync(It.IsAny<BinMovement>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var cmd = new SetOnHandQuantityCommand(new SetOnHandQuantityRequestModel(
            PartId: 3, LocationId: null, Quantity: 40, Reason: "Opening stock",
            Notes: null, SourcePurchaseOrderId: null, VendorId: null));
        await _handler.Handle(cmd, CancellationToken.None);

        added.Should().NotBeNull();
        added!.LocationId.Should().Be(1, "single-location mode places stock at the default location");
        _repo.Verify(r => r.EnsureDefaultLocationAsync(It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.FindLocationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SettingBelowReservedQuantity_throwsAndPersistsNothing()
    {
        var existing = new BinContent { Id = 9, EntityType = "part", EntityId = 3, LocationId = 5, Quantity = 10, ReservedQuantity = 8 };
        _repo.Setup(r => r.GetActiveBinContentsByPartLocationAsync(3, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BinContent> { existing });

        var act = () => _handler.Handle(Cmd(5), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*reserved*");
        _repo.Verify(r => r.AddMovementAsync(It.IsAny<BinMovement>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TwoLotsInOneBin_countBelowTotal_drawsDownOldestLotFirstSoTheTotalMatches()
    {
        var heatA = new BinContent { Id = 1, EntityType = "part", EntityId = 3, LocationId = 5, Quantity = 24, LotNumber = "HEAT-A" };
        var heatB = new BinContent { Id = 2, EntityType = "part", EntityId = 3, LocationId = 5, Quantity = 24, LotNumber = "HEAT-B" };
        _repo.Setup(r => r.GetActiveBinContentsByPartLocationAsync(3, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BinContent> { heatA, heatB });
        var movements = new List<BinMovement>();
        _repo.Setup(r => r.AddMovementAsync(It.IsAny<BinMovement>(), It.IsAny<CancellationToken>()))
            .Callback<BinMovement, CancellationToken>((m, _) => movements.Add(m)).Returns(Task.CompletedTask);

        await _handler.Handle(Cmd(40, "Cycle count"), CancellationToken.None);

        (heatA.Quantity + heatB.Quantity).Should().Be(40);
        heatA.Quantity.Should().Be(16);
        heatB.Quantity.Should().Be(24);
        movements.Should().ContainSingle();
        movements[0].Quantity.Should().Be(8);
        movements[0].FromLocationId.Should().Be(5);
        movements[0].LotNumber.Should().Be("HEAT-A");
        _repo.Verify(r => r.AddBinContentAsync(It.IsAny<BinContent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TwoLotsInOneBin_countAboveTotal_addsToTheUnlottedRow()
    {
        var heatA = new BinContent { Id = 1, EntityType = "part", EntityId = 3, LocationId = 5, Quantity = 24, LotNumber = "HEAT-A" };
        var unlotted = new BinContent { Id = 2, EntityType = "part", EntityId = 3, LocationId = 5, Quantity = 6 };
        _repo.Setup(r => r.GetActiveBinContentsByPartLocationAsync(3, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BinContent> { unlotted, heatA });
        BinMovement? movement = null;
        _repo.Setup(r => r.AddMovementAsync(It.IsAny<BinMovement>(), It.IsAny<CancellationToken>()))
            .Callback<BinMovement, CancellationToken>((m, _) => movement = m).Returns(Task.CompletedTask);

        await _handler.Handle(Cmd(40, "Cycle count"), CancellationToken.None);

        unlotted.Quantity.Should().Be(16);
        heatA.Quantity.Should().Be(24);
        movement!.Quantity.Should().Be(10);
        movement.ToLocationId.Should().Be(5);
        movement.LotNumber.Should().BeNull();
    }

    [Fact]
    public async Task AllStockLotted_countAboveTotal_addsToTheNewestLot()
    {
        var heatA = new BinContent { Id = 1, EntityType = "part", EntityId = 3, LocationId = 5, Quantity = 24, LotNumber = "HEAT-A", PlacedAt = Now.AddDays(-9) };
        var heatB = new BinContent { Id = 2, EntityType = "part", EntityId = 3, LocationId = 5, Quantity = 24, LotNumber = "HEAT-B", PlacedAt = Now.AddDays(-2) };
        _repo.Setup(r => r.GetActiveBinContentsByPartLocationAsync(3, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BinContent> { heatA, heatB });
        _repo.Setup(r => r.AddMovementAsync(It.IsAny<BinMovement>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _handler.Handle(Cmd(50, "Cycle count"), CancellationToken.None);

        heatA.Quantity.Should().Be(24);
        heatB.Quantity.Should().Be(26);
    }

    [Fact]
    public async Task TwoLotsInOneBin_countBelowCombinedReservations_throws()
    {
        var heatA = new BinContent { Id = 1, EntityType = "part", EntityId = 3, LocationId = 5, Quantity = 24, ReservedQuantity = 10, LotNumber = "HEAT-A" };
        var heatB = new BinContent { Id = 2, EntityType = "part", EntityId = 3, LocationId = 5, Quantity = 24, ReservedQuantity = 10, LotNumber = "HEAT-B" };
        _repo.Setup(r => r.GetActiveBinContentsByPartLocationAsync(3, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BinContent> { heatA, heatB });

        var act = () => _handler.Handle(Cmd(15), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*20 unit(s) are reserved*");
        heatA.Quantity.Should().Be(24);
        heatB.Quantity.Should().Be(24);
        _repo.Verify(r => r.AddMovementAsync(It.IsAny<BinMovement>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
