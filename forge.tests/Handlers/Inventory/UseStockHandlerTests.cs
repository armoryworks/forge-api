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

public class UseStockHandlerTests
{
    private readonly Mock<IInventoryRepository> _repo = new();
    private readonly UseStockHandler _handler;

    public UseStockHandlerTests()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "7") }, "Test"));
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext { User = principal });
        _repo.Setup(r => r.FindLocationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageLocation { Id = 5, Name = "A1" });
        _handler = new UseStockHandler(_repo.Object, accessor.Object);
    }

    private static UseStockCommand Cmd(decimal qty, int? location = 5, string? reason = null)
        => new(new UseStockRequestModel(PartId: 3, LocationId: location, Quantity: qty, Reason: reason, Notes: null));

    [Fact]
    public async Task ReducesQuantityAndRecordsIssueMovement()
    {
        var existing = new BinContent { Id = 9, EntityType = "part", EntityId = 3, LocationId = 5, Quantity = 12, ReservedQuantity = 0 };
        _repo.Setup(r => r.FindActiveBinContentByPartLocationAsync(3, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        BinMovement? movement = null;
        _repo.Setup(r => r.AddMovementAsync(It.IsAny<BinMovement>(), It.IsAny<CancellationToken>()))
            .Callback<BinMovement, CancellationToken>((m, _) => movement = m).Returns(Task.CompletedTask);

        await _handler.Handle(Cmd(5, reason: "Consumed on line"), CancellationToken.None);

        existing.Quantity.Should().Be(7);
        movement!.Quantity.Should().Be(5);
        movement.FromLocationId.Should().Be(5);
        movement.ToLocationId.Should().BeNull();
        movement.Reason.Should().Be(BinMovementReason.Issue);
        movement.Notes.Should().Contain("Consumed on line");
    }

    [Fact]
    public async Task UsingAllStock_marksBinRemoved()
    {
        var existing = new BinContent { Id = 9, EntityType = "part", EntityId = 3, LocationId = 5, Quantity = 8, ReservedQuantity = 0 };
        _repo.Setup(r => r.FindActiveBinContentByPartLocationAsync(3, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _repo.Setup(r => r.AddMovementAsync(It.IsAny<BinMovement>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _handler.Handle(Cmd(8), CancellationToken.None);

        existing.Quantity.Should().Be(0);
        existing.RemovedAt.Should().NotBeNull();
        existing.RemovedBy.Should().Be(7);
    }

    [Fact]
    public async Task UsingMoreThanAvailable_respectsReservedAndPersistsNothing()
    {
        var existing = new BinContent { Id = 9, EntityType = "part", EntityId = 3, LocationId = 5, Quantity = 10, ReservedQuantity = 8 };
        _repo.Setup(r => r.FindActiveBinContentByPartLocationAsync(3, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var act = () => _handler.Handle(Cmd(5), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*reserved*");
        existing.Quantity.Should().Be(10);
        _repo.Verify(r => r.AddMovementAsync(It.IsAny<BinMovement>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoStockOnHand_throws()
    {
        _repo.Setup(r => r.FindActiveBinContentByPartLocationAsync(3, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BinContent?)null);

        var act = () => _handler.Handle(Cmd(1), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*on hand*");
        _repo.Verify(r => r.AddMovementAsync(It.IsAny<BinMovement>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact] // Single-location mode: no location supplied -> uses the default location.
    public async Task NoLocationSupplied_usesDefaultLocation()
    {
        _repo.Setup(r => r.EnsureDefaultLocationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageLocation { Id = 1, Name = "Main", IsDefault = true });
        var existing = new BinContent { Id = 9, EntityType = "part", EntityId = 3, LocationId = 1, Quantity = 20, ReservedQuantity = 0 };
        _repo.Setup(r => r.FindActiveBinContentByPartLocationAsync(3, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _repo.Setup(r => r.AddMovementAsync(It.IsAny<BinMovement>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _handler.Handle(Cmd(4, location: null), CancellationToken.None);

        existing.Quantity.Should().Be(16);
        _repo.Verify(r => r.EnsureDefaultLocationAsync(It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.FindLocationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
