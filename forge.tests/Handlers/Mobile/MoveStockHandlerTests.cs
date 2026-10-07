using FluentAssertions;
using MediatR;
using Moq;

using Forge.Api.Features.Inventory;
using Forge.Api.Features.Mobile;
using Forge.Core.Entities;
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
}
