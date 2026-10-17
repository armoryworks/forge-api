using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
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
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
    }
}

public class UpdateJobPartHandler(AppDbContext db) : IRequestHandler<UpdateJobPartCommand, JobPartResponseModel>
{
    public async Task<JobPartResponseModel> Handle(UpdateJobPartCommand request, CancellationToken cancellationToken)
    {
        var jobPart = await db.JobParts
            .Include(jp => jp.Part)
            .FirstOrDefaultAsync(jp => jp.Id == request.JobPartId && jp.JobId == request.JobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Job part with ID {request.JobPartId} not found.");

        if (jobPart.Quantity != request.Quantity)
        {
            var isBuildPart = await db.Jobs.AnyAsync(j => j.Id == request.JobId && j.PartId == jobPart.PartId, cancellationToken);
            db.JobActivityLogs.Add(new JobActivityLog
            {
                JobId = request.JobId,
                UserId = db.CurrentUserId,
                Action = ActivityAction.FieldChanged,
                FieldName = isBuildPart ? "Quantity" : null,
                OldValue = isBuildPart ? $"{jobPart.Quantity:0.####}" : null,
                NewValue = isBuildPart ? $"{request.Quantity:0.####}" : null,
                Description = isBuildPart
                    ? $"Quantity changed from {jobPart.Quantity:0.####} to {request.Quantity:0.####}."
                    : $"Part {jobPart.Part.PartNumber} quantity changed from {jobPart.Quantity:0.####} to {request.Quantity:0.####}.",
            });
        }

        jobPart.Quantity = request.Quantity;
        jobPart.Notes = request.Notes;

        await db.SaveChangesAsync(cancellationToken);

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
