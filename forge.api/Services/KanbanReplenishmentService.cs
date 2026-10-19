using Microsoft.EntityFrameworkCore;
using Npgsql;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Services;

public class KanbanReplenishmentService(
    AppDbContext db,
    IClock clock,
    ILogger<KanbanReplenishmentService> logger) : IKanbanReplenishmentService
{
    private const int CardNumberAttempts = 3;

    public async Task<KanbanCard> CreateCardAsync(CreateKanbanCardRequestModel request, CancellationToken ct)
    {
        if (!await db.Parts.AnyAsync(p => p.Id == request.PartId, ct))
            throw new KeyNotFoundException($"Part {request.PartId} not found");
        if (!await db.WorkCenters.AnyAsync(w => w.Id == request.WorkCenterId, ct))
            throw new KeyNotFoundException($"Work center {request.WorkCenterId} not found");

        var card = new KanbanCard
        {
            PartId = request.PartId,
            WorkCenterId = request.WorkCenterId,
            StorageLocationId = request.StorageLocationId,
            BinQuantity = request.BinQuantity,
            NumberOfBins = request.NumberOfBins,
            SupplySource = request.SupplySource,
            SupplyVendorId = request.SupplyVendorId,
            SupplyWorkCenterId = request.SupplyWorkCenterId,
            LeadTimeDays = request.LeadTimeDays,
            Status = KanbanCardStatus.Full,
            IsActive = true,
        };
        db.KanbanCards.Add(card);

        var next = (await db.KanbanCards.MaxAsync(c => (int?)c.Id, ct) ?? 0) + 1;
        for (var attempt = 1; ; attempt++)
        {
            card.CardNumber = $"KB-{next:D4}";
            try
            {
                await db.SaveChangesAsync(ct);
                break;
            }
            catch (DbUpdateException ex) when (attempt < CardNumberAttempts
                && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                next++;
            }
        }

        logger.LogInformation("Kanban card {CardNumber} created for part {PartId}", card.CardNumber, card.PartId);
        return card;
    }

    public async Task<KanbanCard> UpdateCardAsync(int cardId, UpdateKanbanCardRequestModel request, CancellationToken ct)
    {
        var card = await FindAsync(cardId, ct);

        if (request.BinQuantity is { } binQuantity) card.BinQuantity = binQuantity;
        if (request.NumberOfBins is { } numberOfBins) card.NumberOfBins = numberOfBins;
        if (request.SupplySource is { } supplySource) card.SupplySource = supplySource;
        if (request.SupplyVendorId is not null) card.SupplyVendorId = request.SupplyVendorId;
        if (request.SupplyWorkCenterId is not null) card.SupplyWorkCenterId = request.SupplyWorkCenterId;
        if (request.StorageLocationId is not null) card.StorageLocationId = request.StorageLocationId;
        if (request.LeadTimeDays is not null) card.LeadTimeDays = request.LeadTimeDays;
        if (request.IsActive is { } isActive) card.IsActive = isActive;

        await db.SaveChangesAsync(ct);
        return card;
    }

    public async Task TriggerReplenishmentAsync(int cardId, KanbanTriggerType triggerType, int? triggeredByUserId, CancellationToken ct)
    {
        var card = await FindAsync(cardId, ct);
        if (!card.IsActive)
            throw new InvalidOperationException($"Kanban card {card.CardNumber} is inactive");
        if (card.Status is KanbanCardStatus.Triggered or KanbanCardStatus.InReplenishment)
            throw new InvalidOperationException($"Kanban card {card.CardNumber} is already waiting for replenishment");

        var now = clock.UtcNow;
        card.Status = KanbanCardStatus.Triggered;
        card.LastTriggeredAt = now;
        card.TriggerCount++;
        db.KanbanTriggerLogs.Add(new KanbanTriggerLog
        {
            KanbanCardId = card.Id,
            TriggerType = triggerType,
            TriggeredAt = now,
            RequestedQuantity = card.BinQuantity,
            TriggeredByUserId = triggeredByUserId,
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Kanban card {CardNumber} triggered ({TriggerType})", card.CardNumber, triggerType);
    }

    public async Task ConfirmReplenishmentAsync(int cardId, decimal fulfilledQuantity, CancellationToken ct)
    {
        if (fulfilledQuantity <= 0)
            throw new InvalidOperationException("Fulfilled quantity must be greater than zero");

        var card = await FindAsync(cardId, ct);
        if (card.Status is not (KanbanCardStatus.Triggered or KanbanCardStatus.InReplenishment))
            throw new InvalidOperationException($"Kanban card {card.CardNumber} is not waiting for replenishment");

        var now = clock.UtcNow;
        var openLog = await db.KanbanTriggerLogs
            .Where(l => l.KanbanCardId == card.Id && l.FulfilledAt == null)
            .OrderByDescending(l => l.TriggeredAt)
            .FirstOrDefaultAsync(ct);
        if (openLog is not null)
        {
            openLog.FulfilledAt = now;
            openLog.FulfilledQuantity = fulfilledQuantity;
        }

        card.Status = KanbanCardStatus.Full;
        card.LastReplenishedAt = now;
        card.ActiveOrderId = null;
        card.ActiveOrderType = null;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Kanban card {CardNumber} replenished with {Quantity}", card.CardNumber, fulfilledQuantity);
    }

    public async Task<IReadOnlyList<KanbanCard>> GetCardsByWorkCenterAsync(int workCenterId, CancellationToken ct) =>
        await db.KanbanCards
            .AsNoTracking()
            .Where(c => c.WorkCenterId == workCenterId && c.IsActive)
            .OrderBy(c => c.CardNumber)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<KanbanCard>> GetTriggeredCardsAsync(CancellationToken ct) =>
        await db.KanbanCards
            .AsNoTracking()
            .Where(c => c.Status == KanbanCardStatus.Triggered || c.Status == KanbanCardStatus.InReplenishment)
            .OrderBy(c => c.LastTriggeredAt)
            .ToListAsync(ct);

    public Task CalculateOptimalBinQuantityAsync(int cardId, CancellationToken ct) =>
        throw new NotSupportedException("Optimal bin quantity calculation is not available yet");

    private async Task<KanbanCard> FindAsync(int cardId, CancellationToken ct) =>
        await db.KanbanCards.FirstOrDefaultAsync(c => c.Id == cardId, ct)
            ?? throw new KeyNotFoundException($"Kanban card {cardId} not found");
}
