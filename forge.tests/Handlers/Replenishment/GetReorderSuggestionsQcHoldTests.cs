using FluentAssertions;
using Moq;

using Forge.Api.Features.Replenishment;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Replenishment;

public class GetReorderSuggestionsQcHoldTests
{
    [Fact]
    public async Task Suggestion_carries_the_stock_held_back_by_qc()
    {
        using var db = TestDbContextFactory.Create();
        var location = new StorageLocation { Name = "Bin A" };
        var held = new Part { PartNumber = "RS-1", Name = "Resin", ReorderPoint = 1100m, ReorderQuantity = 2000m };
        var clear = new Part { PartNumber = "RS-2", Name = "Pellet" };
        db.AddRange(location, held, clear);
        await db.SaveChangesAsync();
        db.BinContents.AddRange(
            new BinContent { LocationId = location.Id, EntityId = held.Id, Quantity = 330m, Status = BinContentStatus.QcHold },
            new BinContent { LocationId = location.Id, EntityId = held.Id, Quantity = 70m, Status = BinContentStatus.QcHold },
            new BinContent { LocationId = location.Id, EntityId = held.Id, Quantity = 500m, Status = BinContentStatus.Stored },
            new BinContent { LocationId = location.Id, EntityId = held.Id, Quantity = 40m, Status = BinContentStatus.QcHold, RemovedAt = DateTimeOffset.UnixEpoch },
            new BinContent { LocationId = location.Id, EntityId = clear.Id, Quantity = 10m, Status = BinContentStatus.Stored });
        db.ReorderSuggestions.AddRange(
            new ReorderSuggestion { PartId = held.Id, CurrentStock = 500m, AvailableStock = 500m, SuggestedQuantity = 2000m },
            new ReorderSuggestion { PartId = clear.Id, CurrentStock = 10m, AvailableStock = 10m, SuggestedQuantity = 50m });
        await db.SaveChangesAsync();
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));

        var rows = await new GetReorderSuggestionsHandler(db, new PartSourcingResolver(db), clock.Object)
            .Handle(new GetReorderSuggestionsQuery(null), CancellationToken.None);

        var heldRow = rows.Single(r => r.PartId == held.Id);
        heldRow.QcHoldQuantity.Should().Be(400m);
        heldRow.ReorderPoint.Should().Be(1100m);
        rows.Single(r => r.PartId == clear.Id).QcHoldQuantity.Should().Be(0m);
    }
}
