using FluentAssertions;
using MediatR;
using Moq;

using Forge.Api.Features.Inventory;
using Forge.Api.Features.Mobile;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Mobile;

public class MoveStockHandlerTests
{
    [Fact]
    public async Task Handle_FractionalQuantity_ThrowsPlainMessage()
    {
        using var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "P-100", Name = "Bracket" };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        db.BinContents.Add(new BinContent { LocationId = 1, EntityType = "part", EntityId = part.Id, Quantity = 10m });
        await db.SaveChangesAsync();
        var mediator = new Mock<IMediator>();
        var handler = new MoveStockHandler(db, mediator.Object);

        var act = () => handler.Handle(
            new MoveStockCommand(part.Id, 1, 2, 1.5m, null, "device-1"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Enter a whole number.");
        mediator.Verify(m => m.Send(It.IsAny<TransferStockCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_LotTrackedPartWithUnlottedStock_MovesTheUnlottedRow()
    {
        using var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "P-200", Name = "Insert", TraceabilityType = TraceabilityType.Lot };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        var lotted = new BinContent { LocationId = 1, EntityType = "part", EntityId = part.Id, Quantity = 50m, LotNumber = "LOT-A" };
        var unlotted = new BinContent { LocationId = 1, EntityType = "part", EntityId = part.Id, Quantity = 5m };
        db.BinContents.AddRange(lotted, unlotted);
        await db.SaveChangesAsync();
        var mediator = new Mock<IMediator>();
        TransferStockCommand? sent = null;
        mediator.Setup(m => m.Send(It.IsAny<TransferStockCommand>(), It.IsAny<CancellationToken>()))
            .Callback<object, CancellationToken>((c, _) => sent = (TransferStockCommand)c);
        var handler = new MoveStockHandler(db, mediator.Object);

        var result = await handler.Handle(
            new MoveStockCommand(part.Id, 1, 2, 3m, null, "device-1"), CancellationToken.None);

        sent.Should().NotBeNull();
        sent!.Data.SourceBinContentId.Should().Be(unlotted.Id);
        result.LotNumber.Should().BeNull();
        result.Undo.LotNumber.Should().BeNull();
    }

    [Fact]
    public async Task Handle_LotTrackedPartWithOnlyLottedStock_StillRequiresALot()
    {
        using var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "P-300", Name = "Resin", TraceabilityType = TraceabilityType.Lot };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        db.BinContents.Add(new BinContent { LocationId = 1, EntityType = "part", EntityId = part.Id, Quantity = 50m, LotNumber = "LOT-A" });
        await db.SaveChangesAsync();
        var mediator = new Mock<IMediator>();
        var handler = new MoveStockHandler(db, mediator.Object);

        var act = () => handler.Handle(
            new MoveStockCommand(part.Id, 1, 2, 3m, null, "device-1"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("This part is lot-tracked — pick a lot.");
        mediator.Verify(m => m.Send(It.IsAny<TransferStockCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_LotTrackedPartUnlottedShortButLotHasEnough_DoesNotTakeTheLot()
    {
        using var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "P-400", Name = "Pellet", TraceabilityType = TraceabilityType.Lot };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        db.BinContents.AddRange(
            new BinContent { LocationId = 1, EntityType = "part", EntityId = part.Id, Quantity = 50m, LotNumber = "LOT-A" },
            new BinContent { LocationId = 1, EntityType = "part", EntityId = part.Id, Quantity = 2m });
        await db.SaveChangesAsync();
        var mediator = new Mock<IMediator>();
        var handler = new MoveStockHandler(db, mediator.Object);

        var act = () => handler.Handle(
            new MoveStockCommand(part.Id, 1, 2, 10m, null, "device-1"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Not enough of that part in the from-bin.");
        mediator.Verify(m => m.Send(It.IsAny<TransferStockCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
