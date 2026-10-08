using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Replenishment;

public static class ReplenishmentPlanning
{
    public static decimal SuggestQuantity(Part part, decimal burnRate, int leadTimeDays)
    {
        if (part.ReorderQuantity.HasValue && part.ReorderQuantity.Value > 0)
            return Math.Ceiling(part.ReorderQuantity.Value);

        if (burnRate > 0)
            return Math.Ceiling(burnRate * (leadTimeDays + (part.SafetyStockDays ?? 14)));

        return Math.Ceiling(part.MinStockThreshold ?? 10m);
    }

    public static (int LeadTimeDays, decimal Quantity) PlanMake(
        Part part, IReadOnlyCollection<Operation> routing, decimal burnRate, ShopCalendar calendar, DateOnly start)
    {
        var basis = part.ReorderQuantity is > 0 ? part.ReorderQuantity.Value : 1m;
        var quantity = SuggestQuantity(
            part, burnRate, OperationTimeMath.MakeLeadTimeDays(routing, basis, calendar, start));
        return (OperationTimeMath.MakeLeadTimeDays(routing, quantity, calendar, start), quantity);
    }

    public static async Task<Dictionary<int, List<Operation>>> LoadRoutingsAsync(
        AppDbContext db, IReadOnlyCollection<int> partIds, CancellationToken ct)
    {
        if (partIds.Count == 0)
            return [];

        return (await db.Operations
            .Where(o => partIds.Contains(o.PartId))
            .ToListAsync(ct))
            .GroupBy(o => o.PartId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    public static async Task<Dictionary<int, (decimal Quantity, DateTimeOffset? EarliestDue)>> LoadOpenJobSupplyAsync(
        AppDbContext db, IReadOnlyCollection<int> partIds, CancellationToken ct)
    {
        if (partIds.Count == 0)
            return [];

        var openJobs = await db.Jobs
            .Include(j => j.JobParts)
            .Where(j => j.PartId != null
                && partIds.Contains(j.PartId.Value)
                && !j.IsArchived
                && j.CompletedDate == null
                && j.Disposition == null)
            .ToListAsync(ct);

        return openJobs
            .GroupBy(j => j.PartId!.Value)
            .ToDictionary(
                g => g.Key,
                g => (g.Sum(OperationTimeMath.JobBuildQuantity), g.Min(j => j.DueDate)));
    }
}
