using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.DomainEvents;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

/// <summary>
/// Daily Hangfire job — identifies active jobs where actual costs exceed estimated costs
/// by more than a configurable variance threshold (default 10%) and publishes JobCostOverrunEvent.
/// </summary>
public class CheckJobCostOverrunJob(
    AppDbContext db,
    IJobCostService costService,
    IPublisher publisher,
    ILogger<CheckJobCostOverrunJob> logger)
{
    private const decimal VarianceThreshold = 0.10m; // 10%
    private const int ChunkSize = 100;

    public async Task Execute(CancellationToken ct)
    {
        // Get active jobs with non-zero estimated costs
        var activeJobIds = await db.Jobs
            .AsNoTracking()
            .Where(j => j.DeletedAt == null)
            .Where(j => (j.EstimatedMaterialCost + j.EstimatedLaborCost + j.EstimatedBurdenCost + j.EstimatedSubcontractCost) > 0)
            .Where(j => j.CurrentStage.Name != "Completed" && j.CurrentStage.Name != "Archived")
            .Select(j => j.Id)
            .ToListAsync(ct);

        if (activeJobIds.Count == 0)
        {
            logger.LogInformation("[CheckJobCostOverrun] No active jobs with estimates — skipping");
            return;
        }

        var eventCount = 0;

        for (var offset = 0; offset < activeJobIds.Count; offset += ChunkSize)
        {
            ct.ThrowIfCancellationRequested();

            var chunk = activeJobIds.Skip(offset).Take(ChunkSize).ToList();

            foreach (var jobId in chunk)
            {
                try
                {
                    var summary = await costService.GetCostSummaryAsync(jobId, ct);

                    var estimated = summary.TotalEstimated;
                    if (estimated <= 0) continue;

                    var actual = summary.TotalActual;
                    var variancePercent = (actual - estimated) / estimated;

                    if (variancePercent > VarianceThreshold)
                    {
                        await publisher.Publish(
                            new JobCostOverrunEvent(jobId, estimated, actual, variancePercent), ct);
                        eventCount++;
                    }
                }
                catch (KeyNotFoundException)
                {
                    // Job deleted between query and cost lookup — skip
                }
            }
        }

        logger.LogInformation("[CheckJobCostOverrun] Published {Count} cost-overrun events", eventCount);
    }
}
