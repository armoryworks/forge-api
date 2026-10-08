using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Capabilities;
using Forge.Api.Features.DomainEvents;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record CreateJobCommand(
    string Title,
    string? Description,
    int TrackTypeId,
    int? AssigneeId,
    int? CustomerId,
    JobPriority? Priority,
    DateTimeOffset? DueDate,
    int? PartId = null,
    // #27: optionally associate the new job with an open sales-order line at create time.
    int? SalesOrderLineId = null,
    // Optional caller-supplied job number — gated by jobs.allow_manual_numbers.
    string? JobNumber = null,
    decimal? Quantity = null,
    int? InitialStageId = null) : IRequest<JobDetailResponseModel>;

public class CreateJobCommandValidator : AbstractValidator<CreateJobCommand>
{
    public const string QuantityNeedsPartMessage = "A quantity needs a part to count.";

    public CreateJobCommandValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(200).WithMessage("Title must not exceed 200 characters.");

        RuleFor(x => x.TrackTypeId)
            .GreaterThan(0).WithMessage("TrackTypeId is required.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0).When(x => x.Quantity.HasValue)
            .WithMessage("Quantity must be greater than zero.");

        RuleFor(x => x.PartId)
            .NotNull().When(x => x.Quantity.HasValue && x.SalesOrderLineId is null)
            .WithMessage(QuantityNeedsPartMessage);
    }
}

public class CreateJobHandler(
    IJobRepository jobRepo,
    ITrackTypeRepository trackRepo,
    IMediator mediator,
    IHubContext<BoardHub> boardHub,
    IBarcodeService barcodeService,
    IHttpContextAccessor httpContextAccessor,
    AppDbContext db,
    Forge.Api.Features.SalesOrders.Acceptance.ISalesOrderAcceptanceGate acceptanceGate,
    ICloudFolderAutoCreator folderAutoCreator,
    ISystemSettingRepository systemSettings,
    IBusinessIdentifierService identifiers,
    ICapabilitySnapshotProvider capabilities) : IRequestHandler<CreateJobCommand, JobDetailResponseModel>
{
    // System setting that gates caller-supplied job numbers. Stored as "true"/"false".
    private const string AllowManualJobNumbersKey = "jobs.allow_manual_numbers";
    private const string OrderConfirmedStageCode = "order_confirmed";
    private const string InitialStageNotOnTrackMessage = "Pick a visible status of this order type.";

    public async Task<JobDetailResponseModel> Handle(CreateJobCommand request, CancellationToken cancellationToken)
    {
        if (request.AssigneeId.HasValue)
            await AssigneeComplianceCheck.EnsureCanBeAssigned(db, capabilities, request.AssigneeId.Value, cancellationToken);

        var partId = request.PartId;
        var quantity = request.Quantity;
        var customerId = request.CustomerId;
        var dueDate = request.DueDate;

        // #27: validate the optional SO-line association before creating the job.
        if (request.SalesOrderLineId is int soLineId)
        {
            var line = await db.SalesOrderLines
                .Where(l => l.Id == soLineId)
                .Select(l => new
                {
                    l.SalesOrderId,
                    l.PartId,
                    l.SalesOrder.CustomerId,
                    l.SalesOrder.RequestedDeliveryDate,
                })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"Sales order line {soLineId} not found.");
            // Close the board bypass: a job can't be linked to an SO line unless the SO has
            // customer acceptance proof (no-op when CAP-O2C-SO-ACCEPTANCE is off).
            await acceptanceGate.EnsureReleasableAsync(line.SalesOrderId, cancellationToken);

            partId ??= line.PartId;
            customerId ??= line.CustomerId;
            dueDate ??= line.RequestedDeliveryDate;
            if (quantity is null && partId is int effectivePartId)
                quantity = await SalesOrderLineDefaultQuantity.ComputeAsync(
                    db, soLineId, effectivePartId, null, cancellationToken);

            if (partId is null && request.Quantity.HasValue)
                throw new ValidationException(
                    [new FluentValidation.Results.ValidationFailure(
                        nameof(CreateJobCommand.PartId), CreateJobCommandValidator.QuantityNeedsPartMessage)]);
        }

        var firstStage = await ResolveInitialStageAsync(request, cancellationToken);

        var jobNumber = await ResolveJobNumberAsync(request, cancellationToken);
        var maxPosition = await jobRepo.GetMaxBoardPositionAsync(firstStage.Id, cancellationToken);

        var job = new Job
        {
            JobNumber = jobNumber,
            Title = request.Title,
            Description = request.Description,
            TrackTypeId = request.TrackTypeId,
            CurrentStageId = firstStage.Id,
            AssigneeId = request.AssigneeId,
            CustomerId = customerId,
            Priority = request.Priority ?? JobPriority.Normal,
            DueDate = dueDate,
            BoardPosition = maxPosition + 1,
            PartId = partId,
            SalesOrderLineId = request.SalesOrderLineId,
        };

        // Phase 3 H4 / WU-20 — if this job is being released against a
        // part with a current BOM revision, pin that revision id so future
        // modifications to the BOM don't retroactively alter what this job
        // was built against. Captured at create time so the pin is in place
        // before the row is even saved (single SaveChanges).
        if (partId is int pinPartId)
        {
            var currentRevId = await db.Parts
                .Where(p => p.Id == pinPartId)
                .Select(p => p.CurrentBomRevisionId)
                .FirstOrDefaultAsync(cancellationToken);
            job.BomRevisionIdAtRelease = currentRevId;
        }

        if (partId is int jobPartId)
        {
            job.JobParts.Add(new JobPart
            {
                PartId = jobPartId,
                Quantity = quantity ?? 1m,
            });
        }

        job.ActivityLogs.Add(new JobActivityLog
        {
            Action = ActivityAction.Created,
            Description = request.SalesOrderLineId is int linkedLineId
                ? $"Job {jobNumber} created (linked to SO line #{linkedLineId})."
                : $"Job {jobNumber} created.",
        });

        await jobRepo.AddAsync(job, cancellationToken);
        await jobRepo.SaveChangesAsync(cancellationToken);

        await barcodeService.CreateBarcodeAsync(
            Core.Enums.BarcodeEntityType.Job, job.Id, job.JobNumber, cancellationToken);

        // Record the number in the identifier registry (history + resolution).
        await identifiers.IssueAsync(BusinessEntityType.Job, job.Id, job.JobNumber, cancellationToken);

        var result = await mediator.Send(new GetJobByIdQuery(job.Id), cancellationToken);

        // Broadcast to board group
        await boardHub.Clients.Group($"board:{request.TrackTypeId}")
            .SendAsync("jobCreated", new BoardJobCreatedEvent(
                job.Id, job.JobNumber, job.Title, request.TrackTypeId,
                firstStage.Id, firstStage.Name, job.BoardPosition), cancellationToken);

        // Publish domain event for calendar integration
        var userId = int.Parse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
        if (userId > 0)
            await mediator.Publish(new JobCreatedEvent(job.Id, userId), cancellationToken);

        // Pro Services rollout (D2 dual-path) — best-effort cloud folder
        // auto-anchor when CAP-EXT-CLOUD-STORAGE is enabled and the active
        // FolderMapBundle has a "Job" suggestion. Parent {Customer} context
        // is loaded only when a customer is linked; the path resolver
        // leaves unmatched tokens literal so a Job without a customer
        // still gets a folder anchored at the resolvable subpath.
        var tokenContext = new Dictionary<string, string>
        {
            ["Job"] = job.JobNumber,
        };
        if (job.CustomerId is int custId)
        {
            var customerName = await db.Customers
                .AsNoTracking()
                .Where(c => c.Id == custId)
                .Select(c => string.IsNullOrWhiteSpace(c.CompanyName) ? c.Name : $"{c.Name} ({c.CompanyName})")
                .FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrEmpty(customerName))
            {
                tokenContext["Customer"] = customerName;
            }
        }
        await folderAutoCreator.AutoCreateAsync(
            entityType: "Job", entityId: job.Id, tokenContext, cancellationToken);

        return result;
    }

    private async Task<JobStage> ResolveInitialStageAsync(CreateJobCommand request, CancellationToken ct)
    {
        if (request.InitialStageId is int initialStageId)
        {
            var chosen = await trackRepo.FindStageAsync(initialStageId, ct);
            if (chosen is null || chosen.TrackTypeId != request.TrackTypeId || !chosen.IsActive)
                throw new ValidationException(
                    [new FluentValidation.Results.ValidationFailure(
                        nameof(CreateJobCommand.InitialStageId), InitialStageNotOnTrackMessage)]);
            return chosen;
        }

        if (request.SalesOrderLineId is not null)
        {
            var orderConfirmed = await db.JobStages
                .Where(s => s.TrackTypeId == request.TrackTypeId && s.IsActive && s.Code == OrderConfirmedStageCode)
                .OrderBy(s => s.SortOrder)
                .FirstOrDefaultAsync(ct);
            if (orderConfirmed is not null)
                return orderConfirmed;
        }

        return await trackRepo.FindFirstActiveStageAsync(request.TrackTypeId, ct)
            ?? throw new KeyNotFoundException($"No active stages found for TrackType {request.TrackTypeId}.");
    }

    // Uses a caller-supplied job number when manual numbers are enabled and one
    // was provided; otherwise draws the next number from the DB sequence.
    private async Task<string> ResolveJobNumberAsync(CreateJobCommand request, CancellationToken ct)
    {
        var supplied = request.JobNumber?.Trim();
        if (!string.IsNullOrWhiteSpace(supplied) && await ManualJobNumbersAllowedAsync(ct))
        {
            if (await jobRepo.JobNumberExistsAsync(supplied, null, ct))
                throw new InvalidOperationException($"Job number '{supplied}' is already in use.");
            return supplied;
        }

        return await jobRepo.GenerateNextJobNumberAsync(ct);
    }

    private async Task<bool> ManualJobNumbersAllowedAsync(CancellationToken ct)
    {
        var setting = await systemSettings.FindByKeyAsync(AllowManualJobNumbersKey, ct);
        return setting is not null && bool.TryParse(setting.Value, out var enabled) && enabled;
    }
}
