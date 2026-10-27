using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Imports;

/// <summary>
/// CSV job import: validates every row (dry run) or, with <see cref="Commit"/>, writes them all in one
/// transaction. Numbers from the file are kept (legacy job numbers survive a migration); a blank number
/// draws the next one. A job may land in any stage of its track type, including a completed one, because
/// this is history: no stage-entry side effects run (no accounting documents, no notifications).
/// </summary>
public record ImportJobsCommand(IReadOnlyList<JobImportRowModel> Rows, bool Commit) : IRequest<DataImportResultModel>;

public class ImportJobsHandler(
    AppDbContext db,
    IJobRepository jobs,
    IBarcodeService barcodes,
    IBusinessIdentifierService identifiers,
    IClock clock) : IRequestHandler<ImportJobsCommand, DataImportResultModel>
{
    public const int MaxRows = 5000;
    private const int MaxJobNumberLength = 20;
    private const int MaxTitleLength = 200;

    private sealed record Plan(JobImportRowModel Row, int RowNumber, List<string> Errors)
    {
        public string? Number { get; set; }
        public string Title { get; set; } = string.Empty;
        public int? CustomerId { get; set; }
        public int? PartId { get; set; }
        public JobStage? Stage { get; set; }
        public JobPriority Priority { get; set; } = JobPriority.Normal;
        public bool IsFinalStage { get; set; }
        public int? CreatedId { get; set; }
    }

    public async Task<DataImportResultModel> Handle(ImportJobsCommand request, CancellationToken ct)
    {
        if (request.Rows.Count > MaxRows)
            throw new InvalidOperationException($"An import can hold at most {MaxRows} rows. Split the file.");

        var plans = request.Rows.Select((r, i) => new Plan(r, i + 1, [])).ToList();
        var lookup = await ImportReferenceLookup.LoadAsync(
            db, request.Rows.Select(r => r.Customer), request.Rows.Select(r => r.Part), ct);
        var trackTypes = await db.TrackTypes.AsNoTracking()
            .Where(t => t.IsActive)
            .Include(t => t.Stages.Where(s => s.IsActive))
            .OrderBy(t => t.SortOrder)
            .ToListAsync(ct);
        var defaultTrack = trackTypes.FirstOrDefault(t => t.IsDefault) ?? trackTypes.FirstOrDefault();

        var fileNumbers = plans
            .Select(p => p.Row.JobNumber?.Trim())
            .Where(n => !string.IsNullOrEmpty(n))
            .GroupBy(n => n!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var numbers = fileNumbers.Keys.ToList();
        var existing = (await db.Jobs.IgnoreQueryFilters()
                .Where(j => numbers.Contains(j.JobNumber))
                .Select(j => j.JobNumber)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var plan in plans)
            Validate(plan, lookup, trackTypes, defaultTrack, fileNumbers, existing);

        var invalid = plans.Count(p => p.Errors.Count > 0);
        if (!request.Commit || invalid > 0)
            return Result(plans, committed: false);

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await WriteAsync(plans, ct);
            await tx.CommitAsync(ct);
        }
        return Result(plans, committed: true);
    }

    private static void Validate(
        Plan plan,
        ImportReferenceLookup lookup,
        List<TrackType> trackTypes,
        TrackType? defaultTrack,
        Dictionary<string, int> fileNumbers,
        HashSet<string> existing)
    {
        var row = plan.Row;
        var errors = plan.Errors;

        var number = row.JobNumber?.Trim();
        if (!string.IsNullOrEmpty(number))
        {
            if (number.Length > MaxJobNumberLength)
                errors.Add($"Job number '{number}' is longer than {MaxJobNumberLength} characters.");
            else if (existing.Contains(number))
                errors.Add($"Job number '{number}' is already in use.");
            else if (fileNumbers[number] > 1)
                errors.Add($"Job number '{number}' appears more than once in the file.");
            plan.Number = number;
        }

        plan.CustomerId = lookup.Customer(row.Customer, errors);
        plan.PartId = lookup.Part(row.Part, errors);

        if (row.Quantity is decimal qty)
        {
            if (qty <= 0m) errors.Add("Quantity must be greater than zero.");
            else if (qty > JobQuantityRules.Max) errors.Add(JobQuantityRules.TooLargeMessage);
            else if (!JobQuantityRules.FitsScale(qty)) errors.Add(JobQuantityRules.ScaleMessage);
            if (string.IsNullOrWhiteSpace(row.Part)) errors.Add("A quantity needs a part to count.");
        }

        var title = row.Title?.Trim();
        if (string.IsNullOrEmpty(title))
            title = !string.IsNullOrWhiteSpace(row.Part) ? row.Part.Trim() : number;
        if (string.IsNullOrEmpty(title))
            errors.Add("A title, part or job number is required.");
        else if (title.Length > MaxTitleLength)
            errors.Add($"Title is longer than {MaxTitleLength} characters.");
        plan.Title = title ?? string.Empty;

        if (row.StartDate is { } start && row.DueDate is { } due && due < start)
            errors.Add("Due date is before the start date.");

        if (!string.IsNullOrWhiteSpace(row.Priority))
        {
            if (Enum.TryParse<JobPriority>(row.Priority.Trim(), true, out var priority) && Enum.IsDefined(priority))
                plan.Priority = priority;
            else
                errors.Add($"Unknown priority '{row.Priority.Trim()}'. Use Low, Normal, High or Urgent.");
        }

        var track = defaultTrack;
        if (!string.IsNullOrWhiteSpace(row.TrackType))
        {
            var key = row.TrackType.Trim();
            track = trackTypes.FirstOrDefault(t =>
                string.Equals(t.Name, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(t.Code, key, StringComparison.OrdinalIgnoreCase));
            if (track is null)
                errors.Add($"Unknown track type '{key}'.");
        }
        if (track is null)
        {
            if (string.IsNullOrWhiteSpace(row.TrackType))
                errors.Add("No active track type is set up to import jobs into.");
            return;
        }

        var stages = track.Stages.OrderBy(s => s.SortOrder).ToList();
        if (stages.Count == 0)
        {
            errors.Add($"Track type '{track.Name}' has no active stages.");
            return;
        }
        if (string.IsNullOrWhiteSpace(row.Stage))
            plan.Stage = stages[0];
        else
        {
            var key = row.Stage.Trim();
            plan.Stage = stages.FirstOrDefault(s =>
                string.Equals(s.Name, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(s.Code, key, StringComparison.OrdinalIgnoreCase));
            if (plan.Stage is null)
                errors.Add($"Unknown stage '{key}' for track type '{track.Name}'.");
        }
        plan.IsFinalStage = plan.Stage is not null && plan.Stage.Id == stages[^1].Id;
    }

    private async Task WriteAsync(List<Plan> plans, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var positions = new Dictionary<int, int>();
        var created = new List<(Plan Plan, Job Job)>();
        foreach (var plan in plans)
        {
            var row = plan.Row;
            var stage = plan.Stage!;
            if (!positions.TryGetValue(stage.Id, out var position))
                position = await jobs.GetMaxBoardPositionAsync(stage.Id, ct);
            positions[stage.Id] = ++position;

            plan.Number ??= await jobs.GenerateNextJobNumberAsync(ct);
            var description = row.Description?.Trim();
            if (!string.IsNullOrWhiteSpace(row.CustomerPO))
                description = string.IsNullOrEmpty(description)
                    ? $"Customer PO: {row.CustomerPO.Trim()}"
                    : $"{description}\nCustomer PO: {row.CustomerPO.Trim()}";

            var job = new Job
            {
                JobNumber = plan.Number,
                Title = plan.Title,
                Description = description,
                TrackTypeId = stage.TrackTypeId,
                CurrentStageId = stage.Id,
                CustomerId = plan.CustomerId,
                PartId = plan.PartId,
                Priority = plan.Priority,
                StartDate = row.StartDate,
                DueDate = row.DueDate,
                CompletedDate = row.CompletedDate ?? (plan.IsFinalStage ? row.DueDate ?? now : null),
                BoardPosition = position,
            };
            if (plan.PartId is int partId)
            {
                var pin = await db.Parts.AsNoTracking()
                    .Where(p => p.Id == partId)
                    .Select(p => new { p.CurrentBomRevisionId, p.Revision })
                    .FirstOrDefaultAsync(ct);
                job.BomRevisionIdAtRelease = pin?.CurrentBomRevisionId;
                job.PartRevision = pin?.Revision;
                job.JobParts.Add(new JobPart { PartId = partId, Quantity = row.Quantity ?? 1m });
            }
            job.ActivityLogs.Add(new JobActivityLog
            {
                Action = ActivityAction.Created,
                Description = $"Job {plan.Number} imported from a file.",
            });
            db.Jobs.Add(job);
            created.Add((plan, job));
        }
        await db.SaveChangesAsync(ct);

        foreach (var (plan, job) in created)
        {
            plan.CreatedId = job.Id;
            await barcodes.CreateBarcodeAsync(BarcodeEntityType.Job, job.Id, job.JobNumber, ct);
            await identifiers.IssueAsync(BusinessEntityType.Job, job.Id, job.JobNumber, ct);
        }
        await AdvanceJobNumberSequenceAsync(created.Select(c => c.Job.JobNumber), ct);
    }

    private async Task AdvanceJobNumberSequenceAsync(IEnumerable<string> numbers, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return;
        var highest = numbers
            .Select(n => n.StartsWith("J-", StringComparison.OrdinalIgnoreCase) && int.TryParse(n[2..], out var v) ? v : 0)
            .DefaultIfEmpty(0)
            .Max();
        if (highest > 0)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT setval('job_number_seq', GREATEST((SELECT last_value FROM job_number_seq), {highest}))", ct);
    }

    private static DataImportResultModel Result(List<Plan> plans, bool committed)
    {
        var rows = plans.Select(p => new DataImportRowResultModel(
            p.Row.RowKey,
            p.RowNumber,
            p.Errors.Count > 0 ? "Invalid" : committed ? "Created" : "Valid",
            p.Errors,
            p.Number,
            p.CreatedId)).ToList();
        var invalid = plans.Count(p => p.Errors.Count > 0);
        return new DataImportResultModel(
            plans.Count, plans.Count - invalid, invalid, committed ? plans.Count : 0, committed, rows);
    }
}
