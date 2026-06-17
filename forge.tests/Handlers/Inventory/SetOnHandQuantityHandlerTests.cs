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
    private readonly SetOnHandQuantityHandler _handler;

    public SetOnHandQuantityHandlerTests()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "7") }, "Test"));
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext { User = principal });
        _repo.Setup(r => r.FindLocationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageLocation { Id = 5, Name = "A1" });
        _handler = new SetOnHandQuantityHandler(_repo.Object, accessor.Object);
    }

    private static SetOnHandQuantityCommand Cmd(decimal qty, string reason = "Opening stock", int? po = null, int? vendor = null)
        => new(new SetOnHandQuantityRequestModel(PartId: 3, LocationId: 5, Quantity: qty, Reason: reason,
            Notes: null, SourcePurchaseOrderId: po, VendorId: vendor));

    [Fact]
    public async Task NoExistingContent_createsBinContentAndPositiveMovementCarryingReasonAndProvenance()
    {
        _repo.Setup(r => r.FindActiveBinContentByPartLocationAsync(3, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BinContent?)null);
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
        _repo.Setup(r => r.FindActiveBinContentByPartLocationAsync(3, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
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
        _repo.Setup(r => r.FindActiveBinContentByPartLocationAsync(3, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BinContent?)null);
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
        _repo.Setup(r => r.FindActiveBinContentByPartLocationAsync(3, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var act = () => _handler.Handle(Cmd(5), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*reserved*");
        _repo.Verify(r => r.AddMovementAsync(It.IsAny<BinMovement>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
