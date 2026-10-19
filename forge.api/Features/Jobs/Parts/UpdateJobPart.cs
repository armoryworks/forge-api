using System.Globalization;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Features.Jobs.Operations;
using Forge.Api.Hubs;
using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs.Parts;

public record UpdateJobPartCommand(
    int JobId,
    int JobPartId,
    decimal Quantity,
    string? Notes) : IRequest<JobPartResponseModel>;

public class UpdateJobPartValidator : AbstractValidator<UpdateJobPartCommand>
{
    public UpdateJobPartValidator()
    {
        RuleFor(x => x.JobId).GreaterThan(0);
        RuleFor(x => x.JobPartId).GreaterThan(0);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.Quantity).JobQuantityLimits();
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
    }
}

public class UpdateJobPartHandler(
    AppDbContext db,
    IHttpContextAccessor httpContext,
    IHubContext<BoardHub> boardHub,
    IMediator mediator) : IRequestHandler<UpdateJobPartCommand, JobPartResponseModel>
{
    public const string QuantityRoleMessage = "Only an admin or a manager can change the work order quantity.";

    public async Task<JobPartResponseModel> Handle(UpdateJobPartCommand request, CancellationToken cancellationToken)
    {
        var jobPart = await db.JobParts
            .Include(jp => jp.Part)
            .Include(jp => jp.Job)
            .FirstOrDefaultAsync(jp => jp.Id == request.JobPartId && jp.JobId == request.JobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Job part with ID {request.JobPartId} not found.");

        var job = jobPart.Job;
        var changesBuildQuantity = jobPart.Quantity != request.Quantity && job.PartId == jobPart.PartId;
        if (changesBuildQuantity)
        {
            var user = httpContext.HttpContext?.User;
            if (user?.IsInRole("Admin") != true && user?.IsInRole("Manager") != true)
                throw new ForbiddenException(QuantityRoleMessage);
            await UpdateJobHandler.EnsureQuantityEditableAsync(db, job, cancellationToken);
        }

        if (jobPart.Quantity != request.Quantity)
        {
            var oldText = jobPart.Quantity.ToString("0.####", CultureInfo.InvariantCulture);
            var newText = request.Quantity.ToString("0.####", CultureInfo.InvariantCulture);
            db.JobActivityLogs.Add(new JobActivityLog
            {
                JobId = request.JobId,
                UserId = db.CurrentUserId,
                Action = ActivityAction.FieldChanged,
                FieldName = changesBuildQuantity ? "Quantity" : null,
                OldValue = changesBuildQuantity ? oldText : null,
                NewValue = changesBuildQuantity ? newText : null,
                Description = changesBuildQuantity
                    ? $"Quantity changed from {oldText} to {newText}."
                    : $"Part {jobPart.Part.PartNumber} quantity changed from {oldText} to {newText}.",
            });
        }

        jobPart.Quantity = request.Quantity;
        jobPart.Notes = request.Notes;

        await db.SaveChangesAsync(cancellationToken);

        if (changesBuildQuantity)
            await JobOperationRules.BroadcastJobUpdatedAsync(boardHub, mediator, job, cancellationToken);

        return new JobPartResponseModel(
            jobPart.Id,
            jobPart.JobId,
            jobPart.PartId,
            jobPart.Part.PartNumber,
            jobPart.Part.Description ?? jobPart.Part.Name,
            jobPart.Part.Status.ToString(),
            jobPart.Quantity,
            jobPart.Notes);
    }
}
