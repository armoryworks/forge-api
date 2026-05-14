using MediatR;
using Microsoft.EntityFrameworkCore;

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
            .Select(o => new
            {
                o.Id,
                o.Title,
                o.PartId,
                PartNumber = o.Part.PartNumber,
                o.SetupMinutes,
                o.RunMinutesEach,
                o.RunMinutesLot,
            })
            .ToDictionaryAsync(o => o.Id, cancellationToken);

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
                var estHours = (op.SetupMinutes + op.RunMinutesEach + op.RunMinutesLot) / 60m;
                var jobCount = g.Select(e => e.JobId).Distinct().Count();

                return new TimeByOperationReportRow
                {
                    PartId = op.PartId,
                    PartNumber = op.PartNumber,
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
