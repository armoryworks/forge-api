using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Reports;

public record GetTimeByOperationReportQuery(int? PartId, DateOnly? DateFrom, DateOnly? DateTo)
    : IRequest<List<TimeByOperationReportRow>>;

public class GetTimeByOperationReportHandler(AppDbContext db)
    : IRequestHandler<GetTimeByOperationReportQuery, List<TimeByOperationReportRow>>
{
    public async Task<List<TimeByOperationReportRow>> Handle(
        GetTimeByOperationReportQuery request, CancellationToken cancellationToken)
    {
        var query = db.TimeEntries
            .AsNoTracking()
            .Where(t => t.OperationId.HasValue && t.JobId.HasValue);

        if (request.DateFrom.HasValue)
            query = query.Where(t => t.Date >= request.DateFrom.Value);
        if (request.DateTo.HasValue)
            query = query.Where(t => t.Date <= request.DateTo.Value);

        // Load time entries with operation info
        var entries = await query
            .Select(t => new
            {
                t.OperationId,
                t.JobId,
                t.DurationMinutes,
                t.EntryType,
            })
            .ToListAsync(cancellationToken);

        if (entries.Count == 0)
            return [];

        var operationIds = entries.Select(e => e.OperationId!.Value).Distinct().ToList();

        // Load operations with part info
        var operationsQuery = db.Operations
            .AsNoTracking()
            .Where(o => operationIds.Contains(o.Id));

        if (request.PartId.HasValue)
            operationsQuery = operationsQuery.Where(o => o.PartId == request.PartId.Value);

        var operations = await operationsQuery
            .Include(o => o.Part)
            .ToDictionaryAsync(o => o.Id, cancellationToken);

        var jobIds = entries.Select(e => e.JobId!.Value).Distinct().ToList();
        var jobQuantities = await db.Jobs
            .AsNoTracking()
            .Include(j => j.JobParts)
            .Where(j => jobIds.Contains(j.Id))
            .ToDictionaryAsync(j => j.Id, OperationTimeMath.JobBuildQuantity, cancellationToken);

        Dictionary<(int JobId, int OperationId), decimal>? lifetimeMinutes = null;
        if (request.DateFrom.HasValue || request.DateTo.HasValue)
        {
            var lifetime = await db.TimeEntries
                .AsNoTracking()
                .Where(t => t.JobId.HasValue && t.OperationId.HasValue
                    && jobIds.Contains(t.JobId.Value) && operationIds.Contains(t.OperationId.Value))
                .GroupBy(t => new { JobId = t.JobId!.Value, OperationId = t.OperationId!.Value })
                .Select(g => new { g.Key.JobId, g.Key.OperationId, Minutes = g.Sum(t => (decimal)t.DurationMinutes) })
                .ToListAsync(cancellationToken);
            lifetimeMinutes = lifetime.ToDictionary(x => (x.JobId, x.OperationId), x => x.Minutes);
        }

        var inWindowMinutes = entries
            .GroupBy(e => (JobId: e.JobId!.Value, OperationId: e.OperationId!.Value))
            .ToDictionary(g => g.Key, g => g.Sum(e => (decimal)e.DurationMinutes));

        var grouped = entries
            .Where(e => operations.ContainsKey(e.OperationId!.Value))
            .GroupBy(e => e.OperationId!.Value)
            .Select(g =>
            {
                var op = operations[g.Key];
                var setupEntries = g.Where(e => e.EntryType == TimeEntryType.Setup);
                var runEntries = g.Where(e => e.EntryType == TimeEntryType.Run);
                var totalMinutes = g.Sum(e => (decimal)e.DurationMinutes);
                var totalHours = totalMinutes / 60m;
                var jobs = g.Select(e => e.JobId!.Value).Distinct().ToList();
                var estHours = jobs.Sum(jobId =>
                {
                    var planned = OperationTimeMath.PlannedMinutes(op, jobQuantities.GetValueOrDefault(jobId, 1m));
                    if (lifetimeMinutes is null
                        || !lifetimeMinutes.TryGetValue((jobId, g.Key), out var jobLifetime)
                        || jobLifetime <= 0)
                        return planned;
                    return planned * inWindowMinutes[(jobId, g.Key)] / jobLifetime;
                }) / 60m;
                var jobCount = jobs.Count;

                return new TimeByOperationReportRow
                {
                    PartId = op.PartId,
                    PartNumber = op.Part.PartNumber,
                    OperationId = op.Id,
                    OperationName = op.Title,
                    JobCount = jobCount,
                    AvgSetupMinutes = jobCount > 0 ? setupEntries.Sum(e => (decimal)e.DurationMinutes) / jobCount : 0,
                    AvgRunMinutesPerPiece = jobCount > 0 ? runEntries.Sum(e => (decimal)e.DurationMinutes) / jobCount : 0,
                    TotalHours = totalHours,
                    EstimatedHours = estHours,
                    VariancePercent = estHours > 0 ? (totalHours - estHours) / estHours * 100 : 0,
                };
            })
            .OrderBy(r => r.PartNumber)
            .ThenBy(r => r.OperationName)
            .ToList();

        return grouped;
    }
}
