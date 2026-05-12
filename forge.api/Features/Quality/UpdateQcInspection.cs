using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.DomainEvents;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record UpdateQcInspectionCommand(int Id, UpdateQcInspectionRequestModel Data) : IRequest<QcInspectionResponseModel>;

public class UpdateQcInspectionCommandValidator : AbstractValidator<UpdateQcInspectionCommand>
{
    public UpdateQcInspectionCommandValidator()
    {
        RuleFor(x => x.Data.Status)
            .Must(s => s is null or "InProgress" or "Passed" or "Failed")
            .WithMessage("Status must be InProgress, Passed, or Failed.");
        RuleFor(x => x.Data.Notes).MaximumLength(2000).When(x => x.Data.Notes is not null);
        RuleForEach(x => x.Data.Results).ChildRules(r =>
        {
            r.RuleFor(x => x.Description).NotEmpty().MaximumLength(200);
            r.RuleFor(x => x.MeasuredValue).MaximumLength(200).When(x => x.MeasuredValue is not null);
            r.RuleFor(x => x.Notes).MaximumLength(500).When(x => x.Notes is not null);
        }).When(x => x.Data.Results is not null);
    }
}

public class UpdateQcInspectionHandler(AppDbContext db, IMediator mediator, IHttpContextAccessor httpContext)
    : IRequestHandler<UpdateQcInspectionCommand, QcInspectionResponseModel>
{
    public async Task<QcInspectionResponseModel> Handle(
        UpdateQcInspectionCommand request, CancellationToken cancellationToken)
    {
        var inspection = await db.QcInspections
            .Include(i => i.Results)
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Inspection {request.Id} not found.");

        var data = request.Data;

        if (data.Status is not null)
        {
            inspection.Status = data.Status;
            if (data.Status is "Passed" or "Failed")
                inspection.CompletedAt = DateTimeOffset.UtcNow;
        }

        if (data.Notes is not null)
            inspection.Notes = data.Notes.Trim();

        if (data.Results is not null)
        {
            // Remove existing results and replace
            db.QcInspectionResults.RemoveRange(inspection.Results);

            inspection.Results = data.Results.Select(r => new QcInspectionResult
            {
                InspectionId = inspection.Id,
                ChecklistItemId = r.ChecklistItemId,
                Description = r.Description.Trim(),
                Passed = r.Passed,
                MeasuredValue = r.MeasuredValue?.Trim(),
                Notes = r.Notes?.Trim(),
            }).ToList();
        }

        await db.SaveChangesAsync(cancellationToken);

        if (data.Status == "Failed" && inspection.JobId.HasValue)
        {
            var userId = int.Parse(httpContext.HttpContext!.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            await mediator.Publish(new QcInspectionFailedEvent(inspection.Id, inspection.JobId.Value, userId), cancellationToken);
        }

        return await db.QcInspections
            .AsNoTracking()
            .Include(i => i.Results)
            .Include(i => i.Job)
            .Include(i => i.Template)
            .Where(i => i.Id == inspection.Id)
            .Select(i => new QcInspectionResponseModel(
                i.Id,
                i.JobId,
                i.Job != null ? i.Job.JobNumber : null,
                i.ProductionRunId,
                i.TemplateId,
                i.Template != null ? i.Template.Name : null,
                i.InspectorId,
                db.Users.Where(u => u.Id == i.InspectorId).Select(u => u.FirstName + " " + u.LastName).FirstOrDefault() ?? "",
                i.LotNumber,
                i.Status,
                i.Notes,
                i.CompletedAt,
                i.Results.Select(r => new QcInspectionResultModel(
                    r.Id,
                    r.ChecklistItemId,
                    r.Description,
                    r.Passed,
                    r.MeasuredValue,
                    r.Notes
                )).ToList(),
                i.CreatedAt))
            .FirstAsync(cancellationToken);
    }
}
