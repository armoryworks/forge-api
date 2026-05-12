using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.KanbanReplenishment;

public record GetKanbanBoardByWorkCenterQuery : IRequest<IReadOnlyList<KanbanBoardWorkCenterResponseModel>>;

public class GetKanbanBoardByWorkCenterHandler(AppDbContext db) : IRequestHandler<GetKanbanBoardByWorkCenterQuery, IReadOnlyList<KanbanBoardWorkCenterResponseModel>>
{
    public async Task<IReadOnlyList<KanbanBoardWorkCenterResponseModel>> Handle(GetKanbanBoardByWorkCenterQuery query, CancellationToken cancellationToken)
    {
        var cards = await db.KanbanCards
            .AsNoTracking()
            .Include(c => c.Part)
            .Include(c => c.WorkCenter)
            .Include(c => c.SupplyVendor)
            .Where(c => c.IsActive)
            .OrderBy(c => c.WorkCenter.Name)
            .ThenBy(c => c.CardNumber)
            .ToListAsync(cancellationToken);

        var grouped = cards
            .GroupBy(c => new { c.WorkCenterId, c.WorkCenter.Name })
            .Select(g => new KanbanBoardWorkCenterResponseModel
            {
                WorkCenterId = g.Key.WorkCenterId,
                WorkCenterName = g.Key.Name,
                Cards = g.Select(c => new KanbanCardResponseModel
                {
                    Id = c.Id,
                    CardNumber = c.CardNumber,
                    PartId = c.PartId,
                    PartNumber = c.Part.PartNumber,
                    PartDescription = c.Part.Description ?? "",
                    WorkCenterId = c.WorkCenterId,
                    WorkCenterName = c.WorkCenter.Name,
                    BinQuantity = c.BinQuantity,
                    NumberOfBins = c.NumberOfBins,
                    Status = c.Status.ToString(),
                    SupplySource = c.SupplySource.ToString(),
                    SupplyVendorName = c.SupplyVendor?.CompanyName,
                    LeadTimeDays = c.LeadTimeDays,
                    LastTriggeredAt = c.LastTriggeredAt,
                    LastReplenishedAt = c.LastReplenishedAt,
                    ActiveOrderId = c.ActiveOrderId,
                    ActiveOrderType = c.ActiveOrderType,
                    TriggerCount = c.TriggerCount,
                    IsActive = c.IsActive,
                }).ToList(),
            })
            .ToList();

        return grouped;
    }
}
