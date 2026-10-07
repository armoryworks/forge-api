using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Capabilities;
using Forge.Api.Features.Jobs.ProductionRuns;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record DisposeJobCommand(int Id, DisposeJobRequestModel Data, int UserId) : IRequest<JobDetailResponseModel>;

public class DisposeJobCommandValidator : AbstractValidator<DisposeJobCommand>
{
    public DisposeJobCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Data.Notes).MaximumLength(2000).When(x => x.Data.Notes is not null);
        RuleFor(x => x.Data.Notes)
            .NotEmpty()
            .WithMessage("A reason is required for this disposition.")
            .When(x => DisposeJobHandler.RequiresReason(x.Data.Disposition));
        RuleFor(x => x.Data.GoodQuantity)
            .GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(int.MaxValue)
            .Must(q => q % 1 == 0).WithMessage("Good quantity must be a whole number.")
            .When(x => x.Data.GoodQuantity.HasValue);
        RuleFor(x => x.Data.LocationId).GreaterThan(0).When(x => x.Data.LocationId.HasValue);
    }
}

public class DisposeJobHandler(
    IJobRepository jobRepo,
    IAssetRepository assetRepo,
    IMediator mediator,
    IHubContext<BoardHub> boardHub,
    IClock clock,
    ICapabilitySnapshotProvider capabilities,
    AppDbContext db) : IRequestHandler<DisposeJobCommand, JobDetailResponseModel>
{
    public const string ProductionHistoryMessage =
        "This work order has production history (lots, material, or received stock). Reverse those first.";

    public const string NoPartMessage =
        "This work order has no part, so nothing can be added to inventory.";

    public const string SeveralPartsMessage =
        "This work order has several parts. Set its part before adding it to inventory.";

    public const string OpenRunsMessage =
        "This work order has a production run in progress. Complete or cancel it before adding to inventory.";

    public const string StockingCapability = "CAP-MFG-COMPLETE";

    private const int HeldNotesExcerptLength = 300;

    public static bool RequiresReason(JobDisposition disposition) =>
        disposition is JobDisposition.Scrap or JobDisposition.HoldForReview or JobDisposition.EnteredInError;

    public async Task<JobDetailResponseModel> Handle(DisposeJobCommand request, CancellationToken cancellationToken)
    {
        var job = await jobRepo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Job with ID {request.Id} not found.");

        var disposition = request.Data.Disposition;
        var releasingHold = job.Disposition == JobDisposition.HoldForReview;

        if (job.Disposition.HasValue && !releasingHold)
            throw new InvalidOperationException($"Job {job.JobNumber} has already been disposed as {job.Disposition}.");

        if (releasingHold && disposition == JobDisposition.HoldForReview)
            throw new InvalidOperationException($"Job {job.JobNumber} is already on hold for review.");

        if (disposition == JobDisposition.EnteredInError && await HasProductionHistoryAsync(job.Id, cancellationToken))
            throw new InvalidOperationException(ProductionHistoryMessage);

        StockPlan? stockPlan = null;
        if (disposition == JobDisposition.AddToInventory && capabilities.IsEnabled(StockingCapability))
            stockPlan = await PlanStockAsync(job, request.Data, cancellationToken);

        if (releasingHold)
        {
            job.ActivityLogs.Add(new JobActivityLog
            {
                Action = ActivityAction.StatusChanged,
                FieldName = "Disposition",
                OldValue = JobDisposition.HoldForReview.ToString(),
                NewValue = disposition.ToString(),
                Description = $"Released from hold (was: {Excerpt(job.DispositionNotes)}) and disposed as {disposition}.",
            });
        }

        job.Disposition = disposition;
        job.DispositionNotes = request.Data.Notes?.Trim();
        job.DispositionAt = clock.UtcNow;

        if (disposition == JobDisposition.EnteredInError)
            job.IsArchived = true;

        if (!releasingHold || disposition == JobDisposition.EnteredInError)
        {
            job.ActivityLogs.Add(new JobActivityLog
            {
                Action = disposition == JobDisposition.EnteredInError
                    ? ActivityAction.Archived
                    : ActivityAction.StatusChanged,
                Description = disposition == JobDisposition.EnteredInError
                    ? $"Job {job.JobNumber} marked as entered in error and removed from the board."
                    : $"Job {job.JobNumber} disposed as {disposition}.",
            });
        }

        if (disposition == JobDisposition.CapitalizeAsAsset)
        {
            // Find first associated part (if any) for SourcePartId
            var jobPart = await db.JobParts
                .Where(jp => jp.JobId == job.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var asset = new Asset
            {
                Name = job.Title,
                AssetType = AssetType.Tooling,
                Status = AssetStatus.Active,
                Notes = $"Capitalized from job {job.JobNumber}",
                SourceJobId = job.Id,
                SourcePartId = jobPart?.PartId,
            };

            await assetRepo.AddAsync(asset, cancellationToken);
        }
        else if (stockPlan is not null)
        {
            await StockGoodOutputAsync(job, stockPlan, request, cancellationToken);
        }
        else
        {
            await jobRepo.SaveChangesAsync(cancellationToken);
        }

        await boardHub.Clients.Group($"board:{job.TrackTypeId}")
            .SendAsync("boardUpdated", new { reason = "dispose" }, cancellationToken);

        return await mediator.Send(new GetJobByIdQuery(job.Id), cancellationToken);
    }

    private sealed record StockPlan(int PartId, int TopUpQuantity);

    private async Task<StockPlan> PlanStockAsync(Job job, DisposeJobRequestModel data, CancellationToken ct)
    {
        var stock = await GetJobDispositionStockHandler.LoadAsync(db, job.Id, job.PartId, ct);

        if (stock.HasSeveralParts)
            throw new InvalidOperationException(SeveralPartsMessage);

        if (stock.PartId is not int partId)
            throw new InvalidOperationException(NoPartMessage);

        if (stock.HasOpenRuns)
            throw new InvalidOperationException(OpenRunsMessage);

        var accounted = (long)stock.ReceivedQuantity + stock.RecordedQuantity;
        var goodQuantity = data.GoodQuantity is decimal requested ? (long)requested : accounted;

        if (goodQuantity < accounted)
            throw Invalid(
                "Data.GoodQuantity",
                $"Good quantity cannot be less than the {accounted} already recorded on this work order's "
                + $"production runs ({stock.ReceivedQuantity} received to stock).");

        if (goodQuantity <= 0)
            throw Invalid(
                "Data.GoodQuantity",
                "A good quantity is required to add this work order's part to inventory.");

        if (data.LocationId is int locationId && !await db.StorageLocations.AnyAsync(l => l.Id == locationId
                && l.DeletedAt == null
                && l.IsActive
                && l.LocationType == LocationType.Bin, ct))
            throw Invalid("Data.LocationId", "Choose an active bin to receive the parts into.");

        return new StockPlan(partId, (int)(goodQuantity - accounted));
    }

    private static ValidationException Invalid(string property, string message) =>
        new([new ValidationFailure(property, message)]);

    private async Task StockGoodOutputAsync(
        Job job, StockPlan plan, DisposeJobCommand request, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;

        var runIds = await db.ProductionRuns
            .Where(pr => pr.JobId == job.Id
                && pr.PartId == plan.PartId
                && pr.Status == ProductionRunStatus.Completed
                && pr.ReceivedToStockAt == null
                && pr.CompletedQuantity > 0)
            .OrderBy(pr => pr.Id)
            .Select(pr => pr.Id)
            .ToListAsync(ct);

        if (plan.TopUpQuantity > 0)
        {
            var now = clock.UtcNow;
            var datePrefix = now.ToString("yyyyMMdd");
            var todayCount = await db.ProductionRuns
                .Where(pr => pr.RunNumber.StartsWith($"RUN-{datePrefix}-"))
                .CountAsync(ct);

            var run = new ProductionRun
            {
                JobId = job.Id,
                PartId = plan.PartId,
                RunNumber = $"RUN-{datePrefix}-{todayCount + 1:D3}",
                TargetQuantity = plan.TopUpQuantity,
                CompletedQuantity = plan.TopUpQuantity,
                Status = ProductionRunStatus.Completed,
                CompletedAt = now,
                Notes = $"Recorded by the {JobDisposition.AddToInventory} disposition of job {job.JobNumber}.",
            };
            db.ProductionRuns.Add(run);
            await db.SaveChangesAsync(ct);
            runIds.Add(run.Id);
        }

        foreach (var runId in runIds)
        {
            await mediator.Send(
                new ReceiveProductionRunToStockCommand(job.Id, runId, request.UserId, request.Data.LocationId),
                ct);
        }

        await db.SaveChangesAsync(ct);

        if (tx is not null)
            await tx.CommitAsync(ct);
    }

    private async Task<bool> HasProductionHistoryAsync(int jobId, CancellationToken ct)
    {
        if (await db.LotRecords.AnyAsync(l => l.JobId == jobId, ct))
            return true;

        if (await db.LotConsumptions.AnyAsync(c => c.JobId == jobId, ct))
            return true;

        if (await db.ProductionRuns.AnyAsync(pr => pr.JobId == jobId
                && (pr.ReceivedToStockAt != null || pr.CompletedQuantity > 0 || pr.ScrapQuantity > 0), ct))
            return true;

        var issues = await db.MaterialIssues
            .Where(mi => mi.JobId == jobId)
            .Select(mi => new { mi.IssueType, mi.Quantity })
            .ToListAsync(ct);

        var netIssued = issues.Where(mi => mi.IssueType == MaterialIssueType.Issue).Sum(mi => mi.Quantity)
            - issues.Where(mi => mi.IssueType == MaterialIssueType.Return).Sum(mi => mi.Quantity);

        return netIssued > 0 || issues.Any(mi => mi.IssueType == MaterialIssueType.Scrap);
    }

    private static string Excerpt(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return "no notes";

        return notes.Length <= HeldNotesExcerptLength ? notes : notes[..HeldNotesExcerptLength] + "...";
    }
}
