using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

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
    string? IterationNotes) : IRequest<JobDetailResponseModel>;

public class UpdateJobCommandValidator : AbstractValidator<UpdateJobCommand>
{
    public UpdateJobCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Title).MaximumLength(200).When(x => x.Title is not null);
        RuleFor(x => x.Description).MaximumLength(5000).When(x => x.Description is not null);
    }
}

public class UpdateJobHandler(
    IJobRepository repo,
    IActivityLogRepository actRepo,
    IMediator mediator,
    IHubContext<BoardHub> boardHub,
    IHttpContextAccessor httpContext,
    AppDbContext db) : IRequestHandler<UpdateJobCommand, JobDetailResponseModel>
{
    public async Task<JobDetailResponseModel> Handle(UpdateJobCommand request, CancellationToken cancellationToken)
    {
        var job = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Job with ID {request.Id} not found.");

        if (request.AssigneeId.HasValue)
            await AssigneeComplianceCheck.EnsureCanBeAssigned(db, request.AssigneeId.Value, cancellationToken);

        var userIdClaim = httpContext.HttpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        int? currentUserId = userIdClaim is not null ? int.Parse(userIdClaim.Value) : null;

        var changes = new List<JobActivityLog>();

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

        var result = await mediator.Send(new GetJobByIdQuery(job.Id), cancellationToken);

        // Broadcast to board + job detail subscribers
        var evt = new BoardJobUpdatedEvent(job.Id, result);
        await boardHub.Clients.Group($"board:{job.TrackTypeId}")
            .SendAsync("jobUpdated", evt, cancellationToken);
        await boardHub.Clients.Group($"job:{job.Id}")
            .SendAsync("jobUpdated", evt, cancellationToken);

        return result;
    }
}
