using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.KanbanReplenishment;

public record GetKanbanCardQuery(int Id) : IRequest<KanbanCardDetailResponseModel>;

public class GetKanbanCardHandler(AppDbContext db) : IRequestHandler<GetKanbanCardQuery, KanbanCardDetailResponseModel>
{
    public async Task<KanbanCardDetailResponseModel> Handle(GetKanbanCardQuery query, CancellationToken cancellationToken)
    {
        var card = await db.KanbanCards
            .AsNoTracking()
            .Include(c => c.Part)
            .Include(c => c.WorkCenter)
            .Include(c => c.StorageLocation)
            .Include(c => c.SupplyVendor)
            .Include(c => c.TriggerLogs.OrderByDescending(t => t.TriggeredAt))
            .FirstOrDefaultAsync(c => c.Id == query.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Kanban card {query.Id} not found");

        return new KanbanCardDetailResponseModel
        {
            Card = new KanbanCardResponseModel
            {
                Id = card.Id,
                CardNumber = card.CardNumber,
                PartId = card.PartId,
                PartNumber = card.Part.PartNumber,
                PartDescription = card.Part.Description ?? "",
                WorkCenterId = card.WorkCenterId,
                WorkCenterName = card.WorkCenter.Name,
                StorageLocationId = card.StorageLocationId,
                StorageLocationName = card.StorageLocation?.Name,
                BinQuantity = card.BinQuantity,
                NumberOfBins = card.NumberOfBins,
                Status = card.Status.ToString(),
                SupplySource = card.SupplySource.ToString(),
                SupplyVendorName = card.SupplyVendor?.CompanyName,
                LeadTimeDays = card.LeadTimeDays,
                LastTriggeredAt = card.LastTriggeredAt,
                LastReplenishedAt = card.LastReplenishedAt,
                ActiveOrderId = card.ActiveOrderId,
                ActiveOrderType = card.ActiveOrderType,
                TriggerCount = card.TriggerCount,
                IsActive = card.IsActive,
            },
            TriggerLogs = card.TriggerLogs.Select(t => new KanbanTriggerLogResponseModel
            {
                Id = t.Id,
                TriggerType = t.TriggerType.ToString(),
                TriggeredAt = t.TriggeredAt,
                FulfilledAt = t.FulfilledAt,
                RequestedQuantity = t.RequestedQuantity,
                FulfilledQuantity = t.FulfilledQuantity,
                OrderId = t.OrderId,
                OrderType = t.OrderType,
                TriggeredByName = null,
            }).ToList(),
        };
    }
}
