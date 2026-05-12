using Microsoft.EntityFrameworkCore;

using MediatR;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.SalesOrders;

public record GetSalesOrderScheduleQuery(int SalesOrderId) : IRequest<List<ScheduleMilestoneModel>>;

public class GetSalesOrderScheduleHandler(AppDbContext db, BackwardSchedulingService scheduler, IClock clock)
    : IRequestHandler<GetSalesOrderScheduleQuery, List<ScheduleMilestoneModel>>
{
    public async Task<List<ScheduleMilestoneModel>> Handle(GetSalesOrderScheduleQuery request, CancellationToken cancellationToken)
    {
        var so = await db.SalesOrders
            .Include(s => s.Lines)
                .ThenInclude(l => l.Part)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.SalesOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order {request.SalesOrderId} not found");

        var now = clock.UtcNow;
        var results = new List<ScheduleMilestoneModel>();

        foreach (var line in so.Lines.OrderBy(l => l.LineNumber))
        {
            var schedule = await scheduler.CalculateSchedule(line.Id, cancellationToken);

            var isAtRisk = schedule.PoOrderBy < now
                || schedule.MaterialsNeededBy < now
                || schedule.ProductionStartBy < now
                || schedule.ProductionCompleteBy < now
                || schedule.QcCompleteBy < now
                || schedule.ShipBy < now;

            results.Add(new ScheduleMilestoneModel(
                SalesOrderLineId: line.Id,
                PartNumber: line.Part?.PartNumber,
                PartDescription: line.Part?.Description ?? line.Description,
                DeliveryDate: schedule.DeliveryDate,
                ShipBy: schedule.ShipBy,
                QcCompleteBy: schedule.QcCompleteBy,
                ProductionCompleteBy: schedule.ProductionCompleteBy,
                ProductionStartBy: schedule.ProductionStartBy,
                MaterialsNeededBy: schedule.MaterialsNeededBy,
                PoOrderBy: schedule.PoOrderBy,
                IsAtRisk: isAtRisk));
        }

        return results;
    }
}
