using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record GetJobDispositionStockQuery(int JobId) : IRequest<JobDispositionStockResponseModel>;

public class GetJobDispositionStockHandler(AppDbContext db)
    : IRequestHandler<GetJobDispositionStockQuery, JobDispositionStockResponseModel>
{
    public async Task<JobDispositionStockResponseModel> Handle(
        GetJobDispositionStockQuery request, CancellationToken cancellationToken)
    {
        var jobPartId = await db.Jobs
            .Where(j => j.Id == request.JobId)
            .Select(j => new { j.PartId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Job with ID {request.JobId} not found.");

        return await LoadAsync(db, request.JobId, jobPartId.PartId, cancellationToken);
    }

    public static async Task<JobDispositionStockResponseModel> LoadAsync(
        AppDbContext db, int jobId, int? jobPartId, CancellationToken ct)
    {
        var partId = jobPartId;
        var hasSeveralParts = false;

        if (partId is null)
        {
            var jobParts = await db.JobParts
                .Where(jp => jp.JobId == jobId)
                .Select(jp => jp.PartId)
                .Distinct()
                .Take(2)
                .ToListAsync(ct);

            if (jobParts.Count == 1)
                partId = jobParts[0];
            else
                hasSeveralParts = jobParts.Count > 1;
        }

        if (partId is null)
            return new JobDispositionStockResponseModel(null, hasSeveralParts, null, 0, 0, false);

        var defaultBinId = await db.Parts
            .Where(p => p.Id == partId)
            .Select(p => p.DefaultBinId)
            .FirstOrDefaultAsync(ct);

        var runs = await db.ProductionRuns
            .Where(pr => pr.JobId == jobId && pr.PartId == partId)
            .Select(pr => new { pr.Status, pr.CompletedQuantity, pr.ReceivedQuantity, pr.ReceivedToStockAt })
            .ToListAsync(ct);

        var received = runs
            .Where(r => r.ReceivedToStockAt != null)
            .Sum(r => (long)r.ReceivedQuantity);
        var recorded = runs
            .Where(r => r.ReceivedToStockAt == null && r.Status == ProductionRunStatus.Completed)
            .Sum(r => (long)Math.Max(r.CompletedQuantity, 0));
        var hasOpenRuns = runs.Any(r => r.Status == ProductionRunStatus.InProgress);

        return new JobDispositionStockResponseModel(
            partId,
            false,
            defaultBinId,
            (int)Math.Min(received, int.MaxValue),
            (int)Math.Min(recorded, int.MaxValue),
            hasOpenRuns);
    }
}
