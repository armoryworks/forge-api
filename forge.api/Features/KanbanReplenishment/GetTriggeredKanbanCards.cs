using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.KanbanReplenishment;

public record GetTriggeredKanbanCardsQuery : IRequest<IReadOnlyList<KanbanCardResponseModel>>;

public class GetTriggeredKanbanCardsHandler(AppDbContext db) : IRequestHandler<GetTriggeredKanbanCardsQuery, IReadOnlyList<KanbanCardResponseModel>>
{
    public async Task<IReadOnlyList<KanbanCardResponseModel>> Handle(GetTriggeredKanbanCardsQuery query, CancellationToken cancellationToken)
    {
        return await db.KanbanCards
            .AsNoTracking()
            .Include(c => c.Part)
            .Include(c => c.WorkCenter)
            .Include(c => c.SupplyVendor)
            .Where(c => c.Status == KanbanCardStatus.Triggered || c.Status == KanbanCardStatus.InReplenishment)
            .OrderBy(c => c.LastTriggeredAt)
            .Select(c => new KanbanCardResponseModel
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
                SupplyVendorName = c.SupplyVendor != null ? c.SupplyVendor.CompanyName : null,
                LeadTimeDays = c.LeadTimeDays,
                LastTriggeredAt = c.LastTriggeredAt,
                LastReplenishedAt = c.LastReplenishedAt,
                ActiveOrderId = c.ActiveOrderId,
                ActiveOrderType = c.ActiveOrderType,
                TriggerCount = c.TriggerCount,
                IsActive = c.IsActive,
            })
            .ToListAsync(cancellationToken);
    }
}
