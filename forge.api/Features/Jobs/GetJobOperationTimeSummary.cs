using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record GetJobOperationTimeSummaryQuery(int JobId) : IRequest<List<OperationTimeAnalysisModel>>;

public class GetJobOperationTimeSummaryHandler(AppDbContext db)
    : IRequestHandler<GetJobOperationTimeSummaryQuery, List<OperationTimeAnalysisModel>>
{
    public async Task<List<OperationTimeAnalysisModel>> Handle(
        GetJobOperationTimeSummaryQuery request, CancellationToken cancellationToken)
    {
        var job = await db.Jobs
            .AsNoTracking()
            .Include(j => j.Part)
            .ThenInclude(p => p!.Operations)
            .FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Job {request.JobId} not found");

        if (job.Part?.Operations == null || job.Part.Operations.Count == 0)
            return [];

        var operationIds = job.Part.Operations.Select(o => o.Id).ToList();

        // Batch load all time entries for this job's operations
        var timeEntries = await db.TimeEntries
            .AsNoTracking()
            .Where(t => t.JobId == request.JobId && t.OperationId.HasValue && operationIds.Contains(t.OperationId.Value))
            .ToListAsync(cancellationToken);

        var entriesByOp = timeEntries
            .GroupBy(t => t.OperationId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        return job.Part.Operations
            .OrderBy(o => o.StepNumber)
            .Select(op =>
            {
                var entries = entriesByOp.GetValueOrDefault(op.Id, []);
                var setupMinutes = entries.Where(e => e.EntryType == TimeEntryType.Setup).Sum(e => (decimal)e.DurationMinutes);
                var runMinutes = entries.Where(e => e.EntryType == TimeEntryType.Run).Sum(e => (decimal)e.DurationMinutes);
                var totalMinutes = entries.Sum(e => (decimal)e.DurationMinutes);
                var estSetup = op.SetupMinutes;
                var estRun = op.RunMinutesEach + op.RunMinutesLot;
                var estTotal = estSetup + estRun;

                return new OperationTimeAnalysisModel
                {
                    OperationId = op.Id,
                    OperationName = op.Title,
                    OperationSequence = op.StepNumber,
                    EstimatedSetupMinutes = estSetup,
                    EstimatedRunMinutes = estRun,
                    ActualSetupMinutes = setupMinutes,
                    ActualRunMinutes = runMinutes,
                    ActualTotalMinutes = totalMinutes,
                    SetupVarianceMinutes = setupMinutes - estSetup,
                    RunVarianceMinutes = runMinutes - estRun,
                    EfficiencyPercent = totalMinutes > 0 ? estTotal / totalMinutes * 100 : 0,
                    EntryCount = entries.Count,
                };
            })
            .ToList();
    }
}
