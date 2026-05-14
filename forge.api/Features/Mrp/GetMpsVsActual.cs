using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Mrp;

public record GetMpsVsActualQuery(int MasterScheduleId) : IRequest<List<MpsVsActualResponseModel>>;

public class GetMpsVsActualHandler(AppDbContext db)
    : IRequestHandler<GetMpsVsActualQuery, List<MpsVsActualResponseModel>>
{
    public async Task<List<MpsVsActualResponseModel>> Handle(GetMpsVsActualQuery request, CancellationToken cancellationToken)
    {
        var schedule = await db.MasterSchedules
            .AsNoTracking()
            .Include(s => s.Lines)
                .ThenInclude(l => l.Part)
            .FirstOrDefaultAsync(s => s.Id == request.MasterScheduleId, cancellationToken)
            ?? throw new KeyNotFoundException($"Master schedule {request.MasterScheduleId} not found.");

        // Group planned quantities by part
        var plannedByPart = schedule.Lines
            .GroupBy(l => l.PartId)
            .ToDictionary(g => g.Key, g => new
            {
                Quantity = g.Sum(l => l.Quantity),
                Part = g.First().Part,
            });

        // Get actual completed production runs for these parts within the schedule period
        var partIds = plannedByPart.Keys.ToList();
        var actualByPart = await db.ProductionRuns
            .AsNoTracking()
            .Where(r => partIds.Contains(r.PartId)
                && r.Status == ProductionRunStatus.Completed
                && r.CompletedAt.HasValue
                && r.CompletedAt.Value >= schedule.PeriodStart
                && r.CompletedAt.Value <= schedule.PeriodEnd)
            .GroupBy(r => r.PartId)
            .Select(g => new { PartId = g.Key, Actual = g.Sum(r => (decimal)r.CompletedQuantity) })
            .ToDictionaryAsync(x => x.PartId, x => x.Actual, cancellationToken);

        return plannedByPart.Select(kvp =>
        {
            var planned = kvp.Value.Quantity;
            var actual = actualByPart.GetValueOrDefault(kvp.Key, 0);
            var variance = actual - planned;
            var variancePct = planned != 0 ? Math.Round(variance / planned * 100, 2) : 0;

            return new MpsVsActualResponseModel(
                kvp.Key,
                kvp.Value.Part?.PartNumber ?? "",
                kvp.Value.Part?.Description,
                planned,
                actual,
                variance,
                variancePct
            );
        }).ToList();
    }
}
