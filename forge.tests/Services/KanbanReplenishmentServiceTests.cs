using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

public class KanbanReplenishmentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);

    private static (KanbanReplenishmentService service, AppDbContext db, int partId, int workCenterId) Make()
    {
        var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "KB-PART", Description = "Bin part" };
        var workCenter = new WorkCenter { Name = "Cell", Code = "CELL" };
        db.Parts.Add(part);
        db.WorkCenters.Add(workCenter);
        db.SaveChanges();

        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);
        return (new KanbanReplenishmentService(db, clock.Object, NullLogger<KanbanReplenishmentService>.Instance), db, part.Id, workCenter.Id);
    }

    private static CreateKanbanCardRequestModel Request(int partId, int workCenterId) => new()
    {
        PartId = partId,
        WorkCenterId = workCenterId,
        BinQuantity = 25,
        NumberOfBins = 2,
        SupplySource = KanbanSupplySource.Purchase,
    };

    [Fact]
    public async Task CreateCard_PersistsTheCardWithItsOwnNumber()
    {
        var (service, db, partId, workCenterId) = Make();

        var first = await service.CreateCardAsync(Request(partId, workCenterId), default);
        var second = await service.CreateCardAsync(Request(partId, workCenterId), default);

        var stored = await db.KanbanCards.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
        stored.Should().HaveCount(2);
        stored.Select(c => c.CardNumber).Should().OnlyHaveUniqueItems().And.Equal(first.CardNumber, second.CardNumber);
        stored[0].BinQuantity.Should().Be(25);
        stored[0].Status.Should().Be(KanbanCardStatus.Full);
    }

    [Fact]
    public async Task CreateCard_UnknownPart_Throws()
    {
        var (service, _, _, workCenterId) = Make();

        var act = () => service.CreateCardAsync(Request(9999, workCenterId), default);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UpdateCard_ChangesOnlyTheSuppliedFields()
    {
        var (service, db, partId, workCenterId) = Make();
        var card = await service.CreateCardAsync(Request(partId, workCenterId), default);

        await service.UpdateCardAsync(card.Id, new UpdateKanbanCardRequestModel { BinQuantity = 40 }, default);

        var stored = await db.KanbanCards.AsNoTracking().SingleAsync();
        stored.BinQuantity.Should().Be(40);
        stored.NumberOfBins.Should().Be(2);
        stored.SupplySource.Should().Be(KanbanSupplySource.Purchase);
    }

    [Fact]
    public async Task TriggerThenConfirm_MovesTheCardThroughReplenishmentAndLogsIt()
    {
        var (service, db, partId, workCenterId) = Make();
        var card = await service.CreateCardAsync(Request(partId, workCenterId), default);

        await service.TriggerReplenishmentAsync(card.Id, KanbanTriggerType.Scan, triggeredByUserId: 5, default);

        var triggered = await db.KanbanCards.AsNoTracking().SingleAsync();
        triggered.Status.Should().Be(KanbanCardStatus.Triggered);
        triggered.TriggerCount.Should().Be(1);
        triggered.LastTriggeredAt.Should().Be(Now);
        (await service.GetTriggeredCardsAsync(default)).Should().ContainSingle(c => c.Id == card.Id);

        await service.ConfirmReplenishmentAsync(card.Id, 25, default);

        var replenished = await db.KanbanCards.AsNoTracking().SingleAsync();
        replenished.Status.Should().Be(KanbanCardStatus.Full);
        replenished.LastReplenishedAt.Should().Be(Now);
        var log = await db.KanbanTriggerLogs.AsNoTracking().SingleAsync();
        log.TriggerType.Should().Be(KanbanTriggerType.Scan);
        log.RequestedQuantity.Should().Be(25);
        log.FulfilledQuantity.Should().Be(25);
        log.TriggeredByUserId.Should().Be(5);
    }

    [Fact]
    public async Task Trigger_AlreadyTriggeredCard_Throws()
    {
        var (service, _, partId, workCenterId) = Make();
        var card = await service.CreateCardAsync(Request(partId, workCenterId), default);
        await service.TriggerReplenishmentAsync(card.Id, KanbanTriggerType.Manual, null, default);

        var act = () => service.TriggerReplenishmentAsync(card.Id, KanbanTriggerType.Manual, null, default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Confirm_CardThatWasNotTriggered_Throws()
    {
        var (service, _, partId, workCenterId) = Make();
        var card = await service.CreateCardAsync(Request(partId, workCenterId), default);

        var act = () => service.ConfirmReplenishmentAsync(card.Id, 25, default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Trigger_UnknownCard_Throws()
    {
        var (service, _, _, _) = Make();

        var act = () => service.TriggerReplenishmentAsync(404, KanbanTriggerType.Manual, null, default);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
