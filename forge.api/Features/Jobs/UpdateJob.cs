using System.Globalization;

using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Notifications;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record UpdateJobCommand(
    int Id,
    string? Title,
    string? Description,
    int? AssigneeId,
    int? CustomerId,
    JobPriority? Priority,
    DateTimeOffset? DueDate,
    int? IterationCount,
    string? IterationNotes,
    // Optional caller-supplied job number — editable while the job is not yet
    // disposed, gated by jobs.allow_manual_numbers.
    string? JobNumber = null,
    DateTimeOffset? StartDate = null,
    int? PartId = null,
    decimal? Quantity = null) : IRequest<JobDetailResponseModel>;

public class UpdateJobCommandValidator : AbstractValidator<UpdateJobCommand>
{
    public UpdateJobCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Title).MaximumLength(200).When(x => x.Title is not null);
        RuleFor(x => x.Description).MaximumLength(5000).When(x => x.Description is not null);
        RuleFor(x => x.StartDate)
            .Must((cmd, start) => !UpdateJobHandler.StartsAfterDue(start, cmd.DueDate))
            .WithMessage(UpdateJobHandler.StartAfterDueMessage);
        RuleFor(x => x.Quantity).GreaterThan(0).When(x => x.Quantity is not null);
        RuleFor(x => x.Quantity).JobQuantityLimits();
    }
}

public class UpdateJobHandler(
    IJobRepository repo,
    IActivityLogRepository actRepo,
    IMediator mediator,
    IHubContext<BoardHub> boardHub,
    IHttpContextAccessor httpContext,
    ISystemSettingRepository systemSettings,
    IBusinessIdentifierService identifiers,
    AppDbContext db,
    ICapabilitySnapshotProvider capabilities,
    IStandardCostResolver? standardCost = null,
    IAssignmentNotifier? assignmentNotifier = null) : IRequestHandler<UpdateJobCommand, JobDetailResponseModel>
{
    // System setting that gates caller-supplied job numbers (shared with CreateJob).
    private const string AllowManualJobNumbersKey = "jobs.allow_manual_numbers";

    public const string StartAfterDueMessage = "The start date must be on or before the due date.";
    public const string PartLockedMessage = "The part can't be changed after work has started.";
    public const string QuantityLockedMessage = "The quantity can't be changed on a finished work order.";
    public const string QuantityExplodedMessage =
        "The quantity can't be changed after the BOM has been exploded. Mark its sub-jobs as entered in error first.";

    public static bool StartsAfterDue(DateTimeOffset? start, DateTimeOffset? due) =>
        start.HasValue && due.HasValue && start.Value.UtcDateTime.Date > due.Value.UtcDateTime.Date;

    public async Task<JobDetailResponseModel> Handle(UpdateJobCommand request, CancellationToken cancellationToken)
    {
        var job = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Job with ID {request.Id} not found.");

        if (request.AssigneeId.HasValue)
            await AssigneeComplianceCheck.EnsureCanBeAssigned(db, capabilities, request.AssigneeId.Value, cancellationToken);

        var userIdClaim = httpContext.HttpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        int? currentUserId = userIdClaim is not null ? int.Parse(userIdClaim.Value) : null;

        var changes = new List<JobActivityLog>();

        if ((request.StartDate.HasValue || request.DueDate.HasValue)
            && StartsAfterDue(request.StartDate ?? job.StartDate, request.DueDate ?? job.DueDate))
            throw new ValidationException(
                [new ValidationFailure(nameof(UpdateJobCommand.StartDate), StartAfterDueMessage)]);

        var newPart = request.PartId.HasValue && request.PartId.Value != job.PartId
            ? await CheckPartChangeAsync(job, request.PartId.Value, cancellationToken)
            : null;

        if (request.Quantity.HasValue)
            await CheckQuantityChangeAsync(job, newPart, request.Quantity.Value, cancellationToken);

        // User-settable job number — only while the job is still open (not disposed),
        // manual numbers enabled, and unique (excluding this job). The DB sequence is
        // untouched: an edit renames the human-readable number, it doesn't draw a new one.
        if (request.JobNumber is not null)
        {
            var newNumber = request.JobNumber.Trim();
            if (newNumber.Length > 0 && !string.Equals(newNumber, job.JobNumber, StringComparison.Ordinal))
            {
                if (job.Disposition.HasValue)
                    throw new InvalidOperationException(
                        $"Job {job.JobNumber} has been disposed — its number can no longer be changed.");
                if (!await ManualJobNumbersAllowedAsync(cancellationToken))
                    throw new InvalidOperationException(
                        "Manual work order numbers are turned off. An admin can turn them on in Admin > Settings > Numbering.");
                if (await repo.JobNumberExistsAsync(newNumber, job.Id, cancellationToken))
                    throw new InvalidOperationException($"Job number '{newNumber}' is already in use.");
                await identifiers.IssueAsync(BusinessEntityType.Job, job.Id, job.JobNumber, cancellationToken);
                await identifiers.RenameAsync(BusinessEntityType.Job, job.Id, newNumber, cancellationToken);
                changes.Add(new JobActivityLog
                {
                    JobId = job.Id, UserId = currentUserId, Action = ActivityAction.FieldChanged,
                    FieldName = "JobNumber", OldValue = job.JobNumber, NewValue = newNumber,
                    Description = $"Job number changed from {job.JobNumber} to {newNumber}.",
                });
                job.JobNumber = newNumber;
            }
        }

        if (request.Title is not null && request.Title != job.Title)
        {
            changes.Add(new JobActivityLog
            {
                JobId = job.Id, UserId = currentUserId, Action = ActivityAction.FieldChanged,
                FieldName = "Title", OldValue = job.Title, NewValue = request.Title,
                Description = $"Title changed from \"{job.Title}\" to \"{request.Title}\".",
            });
            job.Title = request.Title;
        }

        if (request.Description is not null && request.Description != job.Description)
        {
            changes.Add(new JobActivityLog
            {
                JobId = job.Id, UserId = currentUserId, Action = ActivityAction.FieldChanged,
                FieldName = "Description", OldValue = null, NewValue = null,
                Description = "Description updated.",
            });
            job.Description = request.Description;
        }

        var reassigned = request.AssigneeId.HasValue && request.AssigneeId.Value != job.AssigneeId;
        if (request.AssigneeId.HasValue && request.AssigneeId.Value != job.AssigneeId)
        {
            var oldAssigneeName = job.AssigneeId.HasValue
                ? await db.Users.Where(u => u.Id == job.AssigneeId.Value)
                    .Select(u => u.LastName + ", " + u.FirstName).FirstOrDefaultAsync(cancellationToken)
                : null;
            var newAssigneeName = await db.Users.Where(u => u.Id == request.AssigneeId.Value)
                .Select(u => u.LastName + ", " + u.FirstName).FirstOrDefaultAsync(cancellationToken);

            changes.Add(new JobActivityLog
            {
                JobId = job.Id, UserId = currentUserId,
                Action = oldAssigneeName is null ? ActivityAction.Assigned : ActivityAction.FieldChanged,
                FieldName = "Assignee", OldValue = oldAssigneeName, NewValue = newAssigneeName,
                Description = oldAssigneeName is null
                    ? $"Assigned to {newAssigneeName}."
                    : $"Assignee changed from {oldAssigneeName} to {newAssigneeName}.",
            });
            job.AssigneeId = request.AssigneeId.Value;
        }

        if (request.CustomerId.HasValue && request.CustomerId.Value != job.CustomerId)
        {
            var oldCustomerName = job.CustomerId.HasValue
                ? await db.Customers.Where(c => c.Id == job.CustomerId.Value)
                    .Select(c => c.Name).FirstOrDefaultAsync(cancellationToken)
                : null;
            var newCustomerName = await db.Customers.Where(c => c.Id == request.CustomerId.Value)
                .Select(c => c.Name).FirstOrDefaultAsync(cancellationToken);

            changes.Add(new JobActivityLog
            {
                JobId = job.Id, UserId = currentUserId, Action = ActivityAction.FieldChanged,
                FieldName = "Customer", OldValue = oldCustomerName, NewValue = newCustomerName,
                Description = oldCustomerName is null
                    ? $"Customer set to {newCustomerName}."
                    : $"Customer changed from {oldCustomerName} to {newCustomerName}.",
            });
            job.CustomerId = request.CustomerId.Value;
        }

        if (request.Priority.HasValue && request.Priority.Value != job.Priority)
        {
            changes.Add(new JobActivityLog
            {
                JobId = job.Id, UserId = currentUserId, Action = ActivityAction.FieldChanged,
                FieldName = "Priority", OldValue = job.Priority.ToString(), NewValue = request.Priority.Value.ToString(),
                Description = $"Priority changed from {job.Priority} to {request.Priority.Value}.",
            });
            job.Priority = request.Priority.Value;
        }

        if (request.DueDate.HasValue && request.DueDate.Value != job.DueDate)
        {
            changes.Add(new JobActivityLog
            {
                JobId = job.Id, UserId = currentUserId, Action = ActivityAction.FieldChanged,
                FieldName = "DueDate",
                OldValue = job.DueDate?.ToString("MM/dd/yyyy"),
                NewValue = request.DueDate.Value.ToString("MM/dd/yyyy"),
                Description = job.DueDate.HasValue
                    ? $"Due date changed from {job.DueDate.Value:MM/dd/yyyy} to {request.DueDate.Value:MM/dd/yyyy}."
                    : $"Due date set to {request.DueDate.Value:MM/dd/yyyy}.",
            });
            job.DueDate = request.DueDate.Value;
        }

        if (request.StartDate.HasValue && request.StartDate.Value != job.StartDate)
        {
            changes.Add(new JobActivityLog
            {
                JobId = job.Id, UserId = currentUserId, Action = ActivityAction.FieldChanged,
                FieldName = "StartDate",
                OldValue = job.StartDate?.ToString("MM/dd/yyyy"),
                NewValue = request.StartDate.Value.ToString("MM/dd/yyyy"),
                Description = job.StartDate.HasValue
                    ? $"Start date changed from {job.StartDate.Value:MM/dd/yyyy} to {request.StartDate.Value:MM/dd/yyyy}."
                    : $"Start date set to {request.StartDate.Value:MM/dd/yyyy}.",
            });
            job.StartDate = request.StartDate.Value;
        }

        if (newPart is not null)
            changes.Add(await ChangePartAsync(job, newPart, currentUserId, cancellationToken));

        if (request.Quantity.HasValue
            && await SetQuantityAsync(job, request.Quantity.Value, currentUserId, cancellationToken) is { } quantityLog)
            changes.Add(quantityLog);

        if (request.IterationCount.HasValue && request.IterationCount.Value != job.IterationCount)
        {
            changes.Add(new JobActivityLog
            {
                JobId = job.Id, UserId = currentUserId, Action = ActivityAction.FieldChanged,
                FieldName = "IterationCount", OldValue = job.IterationCount.ToString(), NewValue = request.IterationCount.Value.ToString(),
                Description = $"Iteration count changed from {job.IterationCount} to {request.IterationCount.Value}.",
            });
            job.IterationCount = request.IterationCount.Value;
        }

        if (request.IterationNotes is not null && request.IterationNotes != job.IterationNotes)
        {
            changes.Add(new JobActivityLog
            {
                JobId = job.Id, UserId = currentUserId, Action = ActivityAction.FieldChanged,
                FieldName = "IterationNotes", OldValue = null, NewValue = null,
                Description = "Iteration notes updated.",
            });
            job.IterationNotes = request.IterationNotes;
        }

        foreach (var log in changes)
            await actRepo.AddAsync(log, cancellationToken);

        await repo.SaveChangesAsync(cancellationToken);

        if (reassigned && assignmentNotifier is not null)
            await assignmentNotifier.JobAssignedAsync(job.Id, job.JobNumber, job.Title, request.AssigneeId!.Value, cancellationToken);

        var result = await mediator.Send(new GetJobByIdQuery(job.Id), cancellationToken);

        // Broadcast to board + job detail subscribers
        var evt = new BoardJobUpdatedEvent(job.Id, result);
        await boardHub.Clients.Group($"board:{job.TrackTypeId}")
            .SendAsync("jobUpdated", evt, cancellationToken);
        await boardHub.Clients.Group($"job:{job.Id}")
            .SendAsync("jobUpdated", evt, cancellationToken);

        return result;
    }

    private async Task<Part> CheckPartChangeAsync(Job job, int newPartId, CancellationToken ct)
    {
        if (job.CompletedDate.HasValue || job.Disposition.HasValue || job.IsArchived
            || await HasStartedWorkAsync(job.Id, ct))
            throw new InvalidOperationException(PartLockedMessage);

        return await db.Parts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == newPartId, ct)
            ?? throw new KeyNotFoundException($"Part with ID {newPartId} not found.");
    }

    private async Task<bool> HasStartedWorkAsync(int jobId, CancellationToken ct) =>
        await db.TimeEntries.AnyAsync(t => t.JobId == jobId, ct)
        || await db.ProductionRuns.AnyAsync(r => r.JobId == jobId, ct)
        || await db.Jobs.AnyAsync(c => c.ParentJobId == jobId, ct)
        || await db.MaterialIssues.AnyAsync(m => m.JobId == jobId, ct)
        || await db.Reservations.AnyAsync(r => r.JobId == jobId, ct)
        || await db.LotConsumptions.AnyAsync(l => l.JobId == jobId, ct)
        || await db.LotRecords.AnyAsync(l => l.JobId == jobId, ct)
        || await db.SerialNumbers.AnyAsync(s => s.JobId == jobId, ct)
        || await db.PurchaseOrders.AnyAsync(p => p.JobId == jobId, ct)
        || await db.SubcontractOrders.AnyAsync(s => s.JobId == jobId, ct);

    private async Task<JobActivityLog> ChangePartAsync(Job job, Part newPart, int? currentUserId, CancellationToken ct)
    {
        var oldPartNumber = job.PartId.HasValue
            ? await db.Parts.Where(p => p.Id == job.PartId.Value).Select(p => p.PartNumber).FirstOrDefaultAsync(ct)
            : null;

        var jobParts = await db.JobParts.Where(jp => jp.JobId == job.Id).ToListAsync(ct);
        var carried = jobParts.FirstOrDefault(jp => jp.PartId == job.PartId);
        var existing = jobParts.FirstOrDefault(jp => jp.PartId == newPart.Id);
        var lineQuantity = job.SalesOrderLineId is int lineId
            ? await SalesOrderLineDefaultQuantity.ComputeAsync(db, lineId, newPart.Id, job.Id, ct)
            : null;
        if (existing is not null)
        {
            if (existing.Quantity <= 1m)
                existing.Quantity = CarriedQuantity(carried?.Quantity ?? existing.Quantity, lineQuantity);
            if (carried is not null)
                db.JobParts.Remove(carried);
        }
        else
        {
            if (carried is not null)
            {
                carried.PartId = newPart.Id;
                carried.Quantity = CarriedQuantity(carried.Quantity, lineQuantity);
            }
            else
                db.JobParts.Add(new JobPart
                {
                    JobId = job.Id,
                    PartId = newPart.Id,
                    Quantity = lineQuantity ?? 1m,
                });
        }

        job.PartId = newPart.Id;
        job.BomRevisionIdAtRelease = newPart.CurrentBomRevisionId;
        job.PartRevision = newPart.Revision;

        return new JobActivityLog
        {
            JobId = job.Id, UserId = currentUserId, Action = ActivityAction.FieldChanged,
            FieldName = "Part", OldValue = oldPartNumber, NewValue = newPart.PartNumber,
            Description = oldPartNumber is null
                ? $"Part set to {newPart.PartNumber}."
                : $"Part changed from {oldPartNumber} to {newPart.PartNumber}.",
        };
    }

    private static decimal CarriedQuantity(decimal carried, decimal? lineQuantity) =>
        carried > 1m ? carried : lineQuantity ?? carried;

    private async Task CheckQuantityChangeAsync(Job job, Part? newPart, decimal quantity, CancellationToken ct)
    {
        if (job.PartId is null && newPart is null)
            throw new ValidationException(
                [new ValidationFailure(nameof(UpdateJobCommand.Quantity), CreateJobCommandValidator.QuantityNeedsPartMessage)]);
        if (newPart is null
            && await db.JobParts.AnyAsync(jp => jp.JobId == job.Id && jp.PartId == job.PartId && jp.Quantity == quantity, ct))
            return;
        await EnsureQuantityEditableAsync(db, job, ct);
    }

    public static async Task EnsureQuantityEditableAsync(AppDbContext db, Job job, CancellationToken ct)
    {
        if (job.CompletedDate.HasValue || job.Disposition.HasValue || job.IsArchived)
            throw new InvalidOperationException(QuantityLockedMessage);
        if (await db.Jobs.AnyAsync(c => c.ParentJobId == job.Id
                && c.DeletedAt == null
                && c.Disposition != JobDisposition.EnteredInError, ct))
            throw new InvalidOperationException(QuantityExplodedMessage);
    }

    private async Task<JobActivityLog?> SetQuantityAsync(Job job, decimal quantity, int? currentUserId, CancellationToken ct)
    {
        await db.JobParts.Where(jp => jp.JobId == job.Id).LoadAsync(ct);
        var buildPart = db.JobParts.Local.FirstOrDefault(jp => jp.JobId == job.Id && jp.PartId == job.PartId);
        var oldQuantity = buildPart?.Quantity;
        if (oldQuantity == quantity)
            return null;

        if (buildPart is null)
            db.JobParts.Add(new JobPart { JobId = job.Id, PartId = job.PartId!.Value, Quantity = quantity });
        else
            buildPart.Quantity = quantity;

        await JobEstimates.SnapshotAsync(db, standardCost, job, quantity, ct);

        var oldText = oldQuantity?.ToString("0.####", CultureInfo.InvariantCulture);
        var newText = quantity.ToString("0.####", CultureInfo.InvariantCulture);
        return new JobActivityLog
        {
            JobId = job.Id, UserId = currentUserId, Action = ActivityAction.FieldChanged,
            FieldName = "Quantity", OldValue = oldText, NewValue = newText,
            Description = oldText is null
                ? $"Quantity set to {newText}."
                : $"Quantity changed from {oldText} to {newText}.",
        };
    }

    private async Task<bool> ManualJobNumbersAllowedAsync(CancellationToken ct)
    {
        var setting = await systemSettings.FindByKeyAsync(AllowManualJobNumbersKey, ct);
        return setting is not null && bool.TryParse(setting.Value, out var enabled) && enabled;
    }
}
