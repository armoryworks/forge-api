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

public class ReceiveStockHandlerTests
{
    private readonly Mock<IInventoryRepository> _repo = new();
    private readonly ReceiveStockHandler _handler;

    public ReceiveStockHandlerTests()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "7") }, "Test"));
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext { User = principal });
        _repo.Setup(r => r.FindLocationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageLocation { Id = 5, Name = "A1" });
        _repo.Setup(r => r.PartExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _handler = new ReceiveStockHandler(_repo.Object, accessor.Object);
    }

    private static ReceiveStockCommand Cmd(decimal qty, int? location = 5, string? reason = null, string? lot = null)
        => new(new ReceiveStockRequestModel(PartId: 3, LocationId: location, Quantity: qty,
            Reason: reason, Notes: null, LotNumber: lot));

    [Fact]
    public async Task NoExistingContent_opensBinAndRecordsReceiveMovement()
    {
        _repo.Setup(r => r.FindActiveBinContentByPartLocationAsync(3, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BinContent?)null);
        BinContent? added = null;
        BinMovement? movement = null;
        _repo.Setup(r => r.AddBinContentAsync(It.IsAny<BinContent>(), It.IsAny<CancellationToken>()))
            .Callback<BinContent, CancellationToken>((c, _) => added = c).Returns(Task.CompletedTask);
        _repo.Setup(r => r.AddMovementAsync(It.IsAny<BinMovement>(), It.IsAny<CancellationToken>()))
            .Callback<BinMovement, CancellationToken>((m, _) => movement = m).Returns(Task.CompletedTask);

        await _handler.Handle(Cmd(25, reason: "Walk-in delivery", lot: "L-99"), CancellationToken.None);

        added.Should().NotBeNull();
        added!.EntityId.Should().Be(3);
        added.LocationId.Should().Be(5);
        added.Quantity.Should().Be(25);
        added.LotNumber.Should().Be("L-99");
        added.PlacedBy.Should().Be(7);
        movement.Should().NotBeNull();
        movement!.Quantity.Should().Be(25);
        movement.ToLocationId.Should().Be(5);
        movement.FromLocationId.Should().BeNull();
        movement.Reason.Should().Be(BinMovementReason.Receive);
        movement.Notes.Should().Contain("Walk-in delivery").And.Contain("Lot L-99");
    }

    [Fact]
    public async Task ExistingContent_addsToQuantityWithoutCreating()
    {
        var existing = new BinContent { Id = 9, EntityType = "part", EntityId = 3, LocationId = 5, Quantity = 5 };
        _repo.Setup(r => r.FindActiveBinContentByPartLocationAsync(3, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        BinMovement? movement = null;
        _repo.Setup(r => r.AddMovementAsync(It.IsAny<BinMovement>(), It.IsAny<CancellationToken>()))
            .Callback<BinMovement, CancellationToken>((m, _) => movement = m).Returns(Task.CompletedTask);

        await _handler.Handle(Cmd(10), CancellationToken.None);

        existing.Quantity.Should().Be(15);
        movement!.Quantity.Should().Be(10);
        movement.Reason.Should().Be(BinMovementReason.Receive);
        movement.Notes.Should().Contain("Manual receipt");
        _repo.Verify(r => r.AddBinContentAsync(It.IsAny<BinContent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact] // B38: a nonexistent partId must be rejected, not silently write a phantom bin.
    public async Task UnknownPart_throwsKeyNotFound()
    {
        _repo.Setup(r => r.PartExistsAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var act = () => _handler.Handle(Cmd(5), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*Part 3*");
        _repo.Verify(r => r.AddBinContentAsync(It.IsAny<BinContent>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.AddMovementAsync(It.IsAny<BinMovement>(), It.IsAny<CancellationToken>()), Times.Never);
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

        await _handler.Handle(Cmd(40, location: null), CancellationToken.None);

        added.Should().NotBeNull();
        added!.LocationId.Should().Be(1, "single-location mode receives into the default location");
        _repo.Verify(r => r.EnsureDefaultLocationAsync(It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.FindLocationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
