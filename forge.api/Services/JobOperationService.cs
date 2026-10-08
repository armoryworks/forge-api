using Microsoft.EntityFrameworkCore;

using Npgsql;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;

namespace Forge.Api.Services;

public class JobOperationService(AppDbContext db, ISettingsService settings, IClock clock) : IJobOperationService
{
    private const int HistoryRowLimit = 20;
    private const string RowUniqueIndex = "ux_job_operations_job_id_operation_id";

    public Task<bool> IsTrackingEnabledAsync(CancellationToken ct)
        => settings.GetBoolAsync(ShopFloorSettings.OperationTrackingKey, ct);

    public async Task<(Job Job, Operation Operation)> FindRoutingStepAsync(int jobId, int operationId, CancellationToken ct)
    {
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found");
        var operation = job.PartId is int partId
            ? await db.Operations.FirstOrDefaultAsync(o => o.Id == operationId && o.PartId == partId, ct)
            : null;
        if (operation is null)
            throw new KeyNotFoundException($"Operation {operationId} is not on job {job.JobNumber}'s routing");
        return (job, operation);
    }

    public async Task<decimal> GetJobQuantityAsync(Job job, CancellationToken ct)
    {
        var quantity = await db.JobParts
            .Where(jp => jp.JobId == job.Id && jp.PartId == job.PartId)
            .SumAsync(jp => jp.Quantity, ct);
        return quantity > 0m ? quantity : 1m;
    }

    public async Task<JobOperation> EnsureRowAsync(Job job, Operation operation, CancellationToken ct)
    {
        var existing = await db.JobOperations
            .FirstOrDefaultAsync(r => r.JobId == job.Id && r.OperationId == operation.Id, ct);
        if (existing is not null)
            return existing;

        var row = new JobOperation
        {
            JobId = job.Id,
            OperationId = operation.Id,
            StepNumber = operation.StepNumber,
            Title = operation.Title,
            Status = JobOperationStatus.NotStarted,
            EstSetupMinutes = operation.SetupMinutes,
            EstRunMinutesEach = Math.Round(OperationTimeMath.PerPieceRunMinutes(operation), 6),
            EstRunMinutesLot = operation.RunMinutesLot,
        };
        db.JobOperations.Add(row);

        try
        {
            await db.SaveChangesAsync(ct);
            return row;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: RowUniqueIndex })
        {
            db.Entry(row).State = EntityState.Detached;
            return await db.JobOperations
                .FirstAsync(r => r.JobId == job.Id && r.OperationId == operation.Id, ct);
        }
    }

    public async Task<JobOperationsResponseModel> BuildAsync(int jobId, CancellationToken ct)
    {
        var job = await db.Jobs
            .AsNoTracking()
            .Include(j => j.JobParts)
            .Include(j => j.Part)
                .ThenInclude(p => p!.Operations)
                    .ThenInclude(o => o.WorkCenter)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found");

        var now = clock.UtcNow;
        var tracking = await IsTrackingEnabledAsync(ct);
        var quantity = OperationTimeMath.JobBuildQuantity(job);
        var routing = (job.Part?.Operations ?? []).OrderBy(o => o.StepNumber).ThenBy(o => o.Id).ToList();
        var routingIds = routing.Select(o => o.Id).ToHashSet();

        var rows = await db.JobOperations
            .AsNoTracking()
            .Where(r => r.JobId == jobId)
            .ToListAsync(ct);
        var rowsByOperation = rows
            .Where(r => r.OperationId.HasValue)
            .GroupBy(r => r.OperationId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var operationIds = routingIds
            .Concat(rows.Where(r => r.OperationId.HasValue).Select(r => r.OperationId!.Value))
            .Distinct()
            .ToList();

        var entries = await db.TimeEntries
            .AsNoTracking()
            .Where(t => t.JobId == jobId && t.OperationId != null && operationIds.Contains(t.OperationId.Value))
            .ToListAsync(ct);
        var entriesByOperation = entries.ToLookup(t => t.OperationId!.Value);

        var userIds = entries.Where(t => t.TimerStart != null && t.TimerStop == null).Select(t => t.UserId)
            .Concat(rows.Where(r => r.CompletedById.HasValue).Select(r => r.CompletedById!.Value))
            .Distinct()
            .ToList();
        var users = userIds.Count == 0
            ? new Dictionary<int, (string Name, string? Initials)>()
            : (await db.Users
                .AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.LastName + ", " + u.FirstName, u.Initials })
                .ToListAsync(ct))
                .ToDictionary(u => u.Id, u => (u.Name, u.Initials));

        var history = await HistoryByOperationAsync(operationIds, ct);

        JobOperationRowResponseModel BuildRow(Operation? op, JobOperation? row)
        {
            var setup = row?.EstSetupMinutes ?? op!.SetupMinutes;
            var each = row?.EstRunMinutesEach ?? OperationTimeMath.PerPieceRunMinutes(op!);
            var lot = row?.EstRunMinutesLot ?? op!.RunMinutesLot;
            var status = row?.Status ?? JobOperationStatus.NotStarted;
            var completed = row?.CompletedQuantity ?? 0m;
            var scrap = row?.ScrapQuantity ?? 0m;
            var operationId = op?.Id ?? row?.OperationId;
            var opEntries = operationId is int id ? entriesByOperation[id].ToList() : new List<TimeEntry>();

            decimal Sum(IEnumerable<TimeEntry> list) => Math.Round(
                list.Sum(t => OperationTimeMath.EntryMinutes(t.TimerStart, t.TimerStop, t.DurationMinutes, now)), 2);

            var actualSetup = Sum(opEntries.Where(t => t.EntryType == TimeEntryType.Setup));
            var actualRun = Sum(opEntries.Where(t => t.EntryType == TimeEntryType.Run));
            var actualTotal = Sum(opEntries);
            var hist = operationId is int hid ? history.GetValueOrDefault(hid) : default;

            return new JobOperationRowResponseModel
            {
                OperationId = operationId,
                JobOperationId = row?.Id,
                Version = row?.Version,
                StepNumber = op?.StepNumber ?? row!.StepNumber,
                Title = op?.Title ?? row!.Title,
                WorkCenterName = op?.WorkCenter?.Name,
                IsRoutingStep = op is not null,
                Status = status,
                CompletedQuantity = completed,
                ScrapQuantity = scrap,
                StartedAt = row?.StartedAt,
                CompletedAt = row?.CompletedAt,
                CompletedByName = row?.CompletedById is int by && users.TryGetValue(by, out var completer)
                    ? completer.Name
                    : null,
                EstimatedSetupMinutes = setup,
                EstimatedRunMinutesEach = each,
                EstimatedRunMinutesLot = lot,
                EstimatedTotalMinutes = Math.Round(setup + lot + each * quantity, 2),
                ActualSetupMinutes = actualSetup,
                ActualRunMinutes = actualRun,
                ActualOtherMinutes = actualTotal - actualSetup - actualRun,
                ActualTotalMinutes = actualTotal,
                ActualRunMinutesEach = completed > 0m ? actualRun / completed : null,
                RemainingMinutes = Math.Round(
                    OperationTimeMath.RemainingMinutes(status, setup, lot, each, quantity, completed, scrap), 2),
                HistoryRunMinutesEach = hist.Completed > 0m ? hist.RunMinutes / hist.Completed : null,
                HistoryJobCount = hist.JobCount,
                OpenTimers = opEntries
                    .Where(t => t.TimerStart != null && t.TimerStop == null)
                    .OrderBy(t => t.TimerStart)
                    .Select(t => new JobOperationOpenTimerResponseModel(
                        t.Id,
                        t.UserId,
                        users.TryGetValue(t.UserId, out var u) ? u.Name : "Unknown",
                        users.TryGetValue(t.UserId, out var ui) ? ui.Initials : null,
                        t.EntryType.ToString(),
                        t.TimerStart!.Value))
                    .ToList(),
            };
        }

        var operations = routing
            .Select(op => BuildRow(op, rowsByOperation.GetValueOrDefault(op.Id)))
            .Concat(rows
                .Where(r => r.OperationId is null || !routingIds.Contains(r.OperationId.Value))
                .OrderBy(r => r.StepNumber)
                .Select(r => BuildRow(null, r)))
            .ToList();

        var routingRows = operations.Where(o => o.IsRoutingStep).ToList();
        var allComplete = routingRows.Count > 0 && routingRows.All(o => IsClosed(o.Status));
        decimal? remaining = job.CompletedDate.HasValue
            ? 0m
            : routingRows.Count == 0 ? null : routingRows.Sum(o => o.RemainingMinutes);

        return new JobOperationsResponseModel(jobId, quantity, tracking, allComplete, remaining, now, operations);
    }

    public async Task<IReadOnlyDictionary<int, JobOperationSummaryResponseModel>> SummarizeAsync(
        IReadOnlyCollection<int> jobIds, CancellationToken ct)
    {
        if (jobIds.Count == 0)
            return new Dictionary<int, JobOperationSummaryResponseModel>();

        var jobs = await db.Jobs
            .AsNoTracking()
            .Where(j => jobIds.Contains(j.Id) && j.PartId != null)
            .Select(j => new
            {
                j.Id,
                PartId = j.PartId!.Value,
                j.CompletedDate,
                Quantity = j.JobParts.Where(jp => jp.PartId == j.PartId).Sum(jp => jp.Quantity),
            })
            .ToListAsync(ct);
        if (jobs.Count == 0)
            return new Dictionary<int, JobOperationSummaryResponseModel>();

        var partIds = jobs.Select(j => j.PartId).Distinct().ToList();
        var routingByPart = (await db.Operations
                .AsNoTracking()
                .Where(o => partIds.Contains(o.PartId))
                .Select(o => new { o.Id, o.PartId, o.StepNumber, o.SetupMinutes, o.RunMinutesEach, o.RunMinutesLot, o.EstimatedMs })
                .ToListAsync(ct))
            .ToLookup(o => o.PartId);

        var jobIdList = jobs.Select(j => j.Id).ToList();
        var rowsByJob = (await db.JobOperations
                .AsNoTracking()
                .Where(r => jobIdList.Contains(r.JobId) && r.OperationId != null)
                .ToListAsync(ct))
            .ToLookup(r => r.JobId);
        var openTimersByJob = (await db.TimeEntries
                .AsNoTracking()
                .Where(t => t.JobId != null && jobIdList.Contains(t.JobId.Value)
                    && t.TimerStart != null && t.TimerStop == null)
                .Select(t => new { JobId = t.JobId!.Value, t.OperationId })
                .ToListAsync(ct))
            .ToLookup(t => t.JobId);

        var result = new Dictionary<int, JobOperationSummaryResponseModel>();
        foreach (var job in jobs)
        {
            var routing = routingByPart[job.PartId].OrderBy(o => o.StepNumber).ToList();
            if (routing.Count == 0)
                continue;

            var quantity = job.Quantity > 0m ? job.Quantity : 1m;
            var rows = rowsByJob[job.Id]
                .GroupBy(r => r.OperationId!.Value)
                .ToDictionary(g => g.Key, g => g.First());
            var openTimers = openTimersByJob[job.Id].ToList();
            var timedOperations = openTimers
                .Where(t => t.OperationId.HasValue)
                .Select(t => t.OperationId!.Value)
                .ToHashSet();

            var complete = 0;
            var inProgress = new List<int>();
            var remaining = 0m;
            foreach (var op in routing)
            {
                var row = rows.GetValueOrDefault(op.Id);
                var status = row?.Status ?? JobOperationStatus.NotStarted;
                if (IsClosed(status))
                    complete++;
                else if (status == JobOperationStatus.InProgress || timedOperations.Contains(op.Id))
                    inProgress.Add(op.StepNumber);

                remaining += OperationTimeMath.RemainingMinutes(
                    status,
                    row?.EstSetupMinutes ?? op.SetupMinutes,
                    row?.EstRunMinutesLot ?? op.RunMinutesLot,
                    row?.EstRunMinutesEach ?? OperationTimeMath.PerPieceRunMinutes(op.RunMinutesEach, op.EstimatedMs),
                    quantity,
                    row?.CompletedQuantity ?? 0m,
                    row?.ScrapQuantity ?? 0m);
            }

            result[job.Id] = new JobOperationSummaryResponseModel(
                job.Id,
                routing.Count,
                complete,
                inProgress,
                openTimers.Count,
                job.CompletedDate.HasValue ? 0m : Math.Round(remaining, 2));
        }

        return result;
    }

    private static bool IsClosed(JobOperationStatus status)
        => status is JobOperationStatus.Complete or JobOperationStatus.Skipped;

    private async Task<Dictionary<int, (decimal RunMinutes, decimal Completed, int JobCount)>> HistoryByOperationAsync(
        List<int> operationIds, CancellationToken ct)
    {
        var completedRows = await db.JobOperations
            .AsNoTracking()
            .Where(r => r.OperationId != null && operationIds.Contains(r.OperationId.Value)
                && r.Status == JobOperationStatus.Complete && r.CompletedQuantity > 0)
            .Select(r => new { r.JobId, OperationId = r.OperationId!.Value, r.CompletedQuantity, r.CompletedAt })
            .ToListAsync(ct);

        var recent = completedRows
            .GroupBy(r => r.OperationId)
            .SelectMany(g => g.OrderByDescending(r => r.CompletedAt).Take(HistoryRowLimit))
            .ToList();
        if (recent.Count == 0)
            return [];

        var jobIds = recent.Select(r => r.JobId).Distinct().ToList();
        var runEntries = await db.TimeEntries
            .AsNoTracking()
            .Where(t => t.EntryType == TimeEntryType.Run && t.JobId != null && t.OperationId != null
                && jobIds.Contains(t.JobId.Value) && operationIds.Contains(t.OperationId.Value))
            .Select(t => new { JobId = t.JobId!.Value, OperationId = t.OperationId!.Value, t.TimerStart, t.TimerStop, t.DurationMinutes })
            .ToListAsync(ct);
        var now = clock.UtcNow;
        var runByJobOperation = runEntries
            .GroupBy(t => (t.JobId, t.OperationId))
            .ToDictionary(
                g => g.Key,
                g => g.Sum(t => OperationTimeMath.EntryMinutes(t.TimerStart, t.TimerStop, t.DurationMinutes, now)));

        return recent
            .GroupBy(r => r.OperationId)
            .ToDictionary(
                g => g.Key,
                g => (
                    g.Sum(r => runByJobOperation.GetValueOrDefault((r.JobId, r.OperationId))),
                    g.Sum(r => r.CompletedQuantity),
                    g.Count()));
    }
}
