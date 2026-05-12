using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.KanbanReplenishment;

public record GetKanbanCardsQuery(int? WorkCenterId, int? PartId, string? Status, int Page = 1, int PageSize = 25) : IRequest<object>;

public class GetKanbanCardsHandler(AppDbContext db) : IRequestHandler<GetKanbanCardsQuery, object>
{
    public async Task<object> Handle(GetKanbanCardsQuery query, CancellationToken cancellationToken)
    {
        var q = db.KanbanCards
            .AsNoTracking()
            .Include(c => c.Part)
            .Include(c => c.WorkCenter)
            .Include(c => c.StorageLocation)
            .Include(c => c.SupplyVendor)
            .AsQueryable();

        if (query.WorkCenterId.HasValue)
            q = q.Where(c => c.WorkCenterId == query.WorkCenterId.Value);

        if (query.PartId.HasValue)
            q = q.Where(c => c.PartId == query.PartId.Value);

        if (!string.IsNullOrEmpty(query.Status) && Enum.TryParse<Core.Enums.KanbanCardStatus>(query.Status, true, out var status))
            q = q.Where(c => c.Status == status);

        var totalCount = await q.CountAsync(cancellationToken);

        var cards = await q
            .OrderBy(c => c.CardNumber)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(c => new KanbanCardResponseModel
            {
                Id = c.Id,
                CardNumber = c.CardNumber,
                PartId = c.PartId,
                PartNumber = c.Part.PartNumber,
                PartDescription = c.Part.Description ?? "",
                WorkCenterId = c.WorkCenterId,
                WorkCenterName = c.WorkCenter.Name,
                StorageLocationId = c.StorageLocationId,
                StorageLocationName = c.StorageLocation != null ? c.StorageLocation.Name : null,
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

        return new
        {
            data = cards,
            page = query.Page,
            pageSize = query.PageSize,
            totalCount,
            totalPages = (int)Math.Ceiling((double)totalCount / query.PageSize),
        };
    }
}
