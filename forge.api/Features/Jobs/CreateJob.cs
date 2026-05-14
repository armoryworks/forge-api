using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Features.DomainEvents;
using Forge.Api.Hubs;
using Forge.Core.Entities;
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
    int? PartId = null) : IRequest<JobDetailResponseModel>;

public class CreateJobCommandValidator : AbstractValidator<CreateJobCommand>
{
    public CreateJobCommandValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(200).WithMessage("Title must not exceed 200 characters.");

        RuleFor(x => x.TrackTypeId)
            .GreaterThan(0).WithMessage("TrackTypeId is required.");
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
    ICloudFolderAutoCreator folderAutoCreator) : IRequestHandler<CreateJobCommand, JobDetailResponseModel>
{
    public async Task<JobDetailResponseModel> Handle(CreateJobCommand request, CancellationToken cancellationToken)
    {
        if (request.AssigneeId.HasValue)
            await AssigneeComplianceCheck.EnsureCanBeAssigned(db, request.AssigneeId.Value, cancellationToken);

        var firstStage = await trackRepo.FindFirstActiveStageAsync(request.TrackTypeId, cancellationToken)
            ?? throw new KeyNotFoundException($"No active stages found for TrackType {request.TrackTypeId}.");

        var jobNumber = await jobRepo.GenerateNextJobNumberAsync(cancellationToken);
        var maxPosition = await jobRepo.GetMaxBoardPositionAsync(firstStage.Id, cancellationToken);

        var job = new Job
        {
            JobNumber = jobNumber,
            Title = request.Title,
            Description = request.Description,
            TrackTypeId = request.TrackTypeId,
            CurrentStageId = firstStage.Id,
            AssigneeId = request.AssigneeId,
            CustomerId = request.CustomerId,
            Priority = request.Priority ?? JobPriority.Normal,
            DueDate = request.DueDate,
            BoardPosition = maxPosition + 1,
            PartId = request.PartId,
        };

        // Phase 3 H4 / WU-20 — if this job is being released against a
        // part with a current BOM revision, pin that revision id so future
        // modifications to the BOM don't retroactively alter what this job
        // was built against. Captured at create time so the pin is in place
        // before the row is even saved (single SaveChanges).
        if (request.PartId is int pinPartId)
        {
            var currentRevId = await db.Parts
                .Where(p => p.Id == pinPartId)
                .Select(p => p.CurrentBomRevisionId)
                .FirstOrDefaultAsync(cancellationToken);
            job.BomRevisionIdAtRelease = currentRevId;
        }

        job.ActivityLogs.Add(new JobActivityLog
        {
            Action = ActivityAction.Created,
            Description = $"Job {jobNumber} created.",
        });

        await jobRepo.AddAsync(job, cancellationToken);
        await jobRepo.SaveChangesAsync(cancellationToken);

        await barcodeService.CreateBarcodeAsync(
            BarcodeEntityType.Job, job.Id, job.JobNumber, cancellationToken);

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
}
