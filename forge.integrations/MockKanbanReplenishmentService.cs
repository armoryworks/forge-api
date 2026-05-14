using Microsoft.Extensions.Logging;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class MockKanbanReplenishmentService(ILogger<MockKanbanReplenishmentService> logger) : IKanbanReplenishmentService
{
    public Task<KanbanCard> CreateCardAsync(CreateKanbanCardRequestModel request, CancellationToken ct)
    {
        logger.LogInformation("[MockKanban] CreateCard Part {PartId}, WC {WorkCenterId}", request.PartId, request.WorkCenterId);
        var card = new KanbanCard
        {
            Id = 1,
            CardNumber = "KB-0001",
            PartId = request.PartId,
            WorkCenterId = request.WorkCenterId,
            BinQuantity = request.BinQuantity,
            NumberOfBins = request.NumberOfBins,
            SupplySource = request.SupplySource,
        };
        return Task.FromResult(card);
    }

    public Task<KanbanCard> UpdateCardAsync(int cardId, UpdateKanbanCardRequestModel request, CancellationToken ct)
    {
        logger.LogInformation("[MockKanban] UpdateCard {CardId}", cardId);
        var card = new KanbanCard { Id = cardId, CardNumber = $"KB-{cardId:D4}" };
        return Task.FromResult(card);
    }

    public Task TriggerReplenishmentAsync(int cardId, KanbanTriggerType triggerType, int? triggeredByUserId, CancellationToken ct)
    {
        logger.LogInformation("[MockKanban] TriggerReplenishment Card {CardId}, Type {TriggerType}", cardId, triggerType);
        return Task.CompletedTask;
    }

    public Task ConfirmReplenishmentAsync(int cardId, decimal fulfilledQuantity, CancellationToken ct)
    {
        logger.LogInformation("[MockKanban] ConfirmReplenishment Card {CardId}, Qty {Quantity}", cardId, fulfilledQuantity);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<KanbanCard>> GetCardsByWorkCenterAsync(int workCenterId, CancellationToken ct)
    {
        logger.LogInformation("[MockKanban] GetCardsByWorkCenter {WorkCenterId}", workCenterId);
        return Task.FromResult<IReadOnlyList<KanbanCard>>([]);
    }

    public Task<IReadOnlyList<KanbanCard>> GetTriggeredCardsAsync(CancellationToken ct)
    {
        logger.LogInformation("[MockKanban] GetTriggeredCards");
        return Task.FromResult<IReadOnlyList<KanbanCard>>([]);
    }

    public Task CalculateOptimalBinQuantityAsync(int cardId, CancellationToken ct)
    {
        logger.LogInformation("[MockKanban] CalculateOptimalBinQuantity Card {CardId}", cardId);
        return Task.CompletedTask;
    }
}
