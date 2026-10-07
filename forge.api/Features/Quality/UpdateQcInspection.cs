using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.DomainEvents;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

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

public class UpdateQcInspectionHandler(AppDbContext db, IMediator mediator, IHttpContextAccessor httpContext, IClock clock)
    : IRequestHandler<UpdateQcInspectionCommand, QcInspectionResponseModel>
{
    public async Task<QcInspectionResponseModel> Handle(
        UpdateQcInspectionCommand request, CancellationToken cancellationToken)
    {
        var inspection = await db.QcInspections
            .Include(i => i.Results)
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Inspection {request.Id} not found.");

        if (inspection.Status is "Passed" or "Failed")
            throw new InvalidOperationException(
                $"Inspection {inspection.Id} is {inspection.Status} and can no longer be changed.");

        var data = request.Data;
        var changedFields = new List<string>();

        if (data.Notes is not null)
        {
            var notes = data.Notes.Trim();
            if (notes != inspection.Notes)
            {
                inspection.Notes = notes;
                changedFields.Add("notes");
            }
        }

        if (data.Results is not null)
        {
            ApplyResults(inspection, data.Results);
            changedFields.Add("results");
        }

        var completing = data.Status is "Passed" or "Failed";
        if (data.Status == "Passed")
            await EnsureRequiredItemsPassedAsync(inspection, cancellationToken);

        if (data.Status is not null && data.Status != inspection.Status)
        {
            inspection.Status = data.Status;
            changedFields.Add("status");
        }

        if (completing)
            inspection.CompletedAt = clock.UtcNow;

        if (completing || changedFields.Count > 0)
        {
            var description = completing
                ? $"Inspection QC #{inspection.Id} {data.Status!.ToLowerInvariant()}"
                : $"Updated {changedFields.Count} field{(changedFields.Count == 1 ? "" : "s")}: {string.Join(", ", changedFields)}";
            var action = completing ? $"inspection-{data.Status!.ToLowerInvariant()}" : "updated";
            var points = new List<(string, int)> { ("QcInspection", inspection.Id) };
            if (inspection.JobId is int jobId)
                points.Add(("Job", jobId));
            db.LogActivityAt(action, description, [.. points]);
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

    private void ApplyResults(QcInspection inspection, List<UpdateQcInspectionResultModel> incoming)
    {
        var keptIds = incoming.Where(r => r.Id.HasValue).Select(r => r.Id!.Value).ToHashSet();
        var unknown = keptIds.Where(id => inspection.Results.All(r => r.Id != id)).ToList();
        if (unknown.Count > 0)
            throw new KeyNotFoundException(
                $"Inspection {inspection.Id} has no result {string.Join(", ", unknown)}.");

        foreach (var stale in inspection.Results.Where(r => !keptIds.Contains(r.Id)).ToList())
        {
            inspection.Results.Remove(stale);
            db.QcInspectionResults.Remove(stale);
        }

        foreach (var model in incoming)
        {
            var row = model.Id is int id ? inspection.Results.First(r => r.Id == id) : null;
            if (row is null)
            {
                row = new QcInspectionResult { InspectionId = inspection.Id, ChecklistItemId = model.ChecklistItemId };
                inspection.Results.Add(row);
            }

            row.Description = model.Description.Trim();
            row.Passed = model.Passed;
            row.MeasuredValue = model.MeasuredValue?.Trim();
            row.Notes = model.Notes?.Trim();
        }
    }

    private async Task EnsureRequiredItemsPassedAsync(QcInspection inspection, CancellationToken cancellationToken)
    {
        if (inspection.TemplateId is not int templateId)
            return;

        var requiredItems = await db.QcChecklistItems
            .AsNoTracking()
            .Where(i => i.TemplateId == templateId && i.IsRequired)
            .OrderBy(i => i.SortOrder)
            .Select(i => new { i.Id, i.Description })
            .ToListAsync(cancellationToken);

        var failed = requiredItems
            .Where(item => !inspection.Results.Any(r => r.ChecklistItemId == item.Id && r.Passed))
            .Select(item => item.Description)
            .ToList();

        if (failed.Count > 0)
            throw new InvalidOperationException(
                $"Inspection {inspection.Id} cannot pass: required checklist items did not pass ({string.Join(", ", failed)}).");
    }
}
