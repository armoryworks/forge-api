using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ShopFloor;

public record GetKioskAvailableJobsQuery(int? TeamId, string? Search, int Take = 50)
    : IRequest<List<KioskAvailableJobResponseModel>>;

public class GetKioskAvailableJobsHandler(AppDbContext db)
    : IRequestHandler<GetKioskAvailableJobsQuery, List<KioskAvailableJobResponseModel>>
{
    public const int MaxTake = 200;

    public async Task<List<KioskAvailableJobResponseModel>> Handle(
        GetKioskAvailableJobsQuery request, CancellationToken ct)
    {
        var query = KioskWork.ReadyToStart(db).AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(j => j.JobNumber.ToLower().Contains(term)
                || j.Title.ToLower().Contains(term)
                || (j.Part != null && j.Part.PartNumber.ToLower().Contains(term)));
        }

        var take = Math.Clamp(request.Take, 1, MaxTake);

        var jobs = await query
            .OrderBy(j => j.DueDate == null)
            .ThenBy(j => j.DueDate)
            .ThenByDescending(j => j.Priority)
            .ThenBy(j => j.JobNumber)
            .Take(take)
            .Select(j => new
            {
                j.Id,
                j.JobNumber,
                j.Title,
                j.PartId,
                PartNumber = j.Part != null ? j.Part.PartNumber : null,
                Quantity = j.JobParts.Where(jp => jp.PartId == j.PartId).Sum(jp => jp.Quantity),
                j.DueDate,
                j.Priority,
                StageName = j.CurrentStage.Name,
            })
            .ToListAsync(ct);

        var nextOperations = await KioskWork.NextOperationsAsync(
            db, jobs.Select(j => (j.Id, j.PartId)), ct);

        return jobs.Select(j => new KioskAvailableJobResponseModel(
            j.Id,
            j.JobNumber,
            j.Title,
            j.PartNumber,
            j.Quantity > 0m ? j.Quantity : 1m,
            j.DueDate,
            j.Priority.ToString(),
            j.StageName,
            nextOperations.GetValueOrDefault(j.Id))).ToList();
    }
}
