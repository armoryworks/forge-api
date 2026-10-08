using FluentValidation;
using FluentValidation.Results;
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

        if (data.Results is not null && ApplyResults(inspection, data.Results))
            changedFields.Add("results");

        var completing = data.Status is "Passed" or "Failed";
        if (completing)
            EnsureRequiredItemsChecked(inspection);
        if (data.Status == "Passed")
            EnsureRequiredItemsPassed(inspection);

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

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException(
                $"Inspection {inspection.Id} was completed or changed by someone else. Reload it and try again.");
        }

        if (data.Status == "Failed" && inspection.JobId.HasValue)
        {
            var userId = int.Parse(httpContext.HttpContext!.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            await mediator.Publish(new QcInspectionFailedEvent(inspection.Id, inspection.JobId.Value, userId), cancellationToken);
        }

        return await QcInspectionMapping.LoadResponseAsync(db, inspection.Id, cancellationToken);
    }

    private bool ApplyResults(QcInspection inspection, List<UpdateQcInspectionResultModel> incoming)
    {
        var keptIds = incoming.Where(r => r.Id.HasValue).Select(r => r.Id!.Value).ToHashSet();
        var unknown = keptIds.Where(id => inspection.Results.All(r => r.Id != id)).ToList();
        if (unknown.Count > 0)
            throw new ValidationException(
            [
                new ValidationFailure(
                    nameof(UpdateQcInspectionRequestModel.Results),
                    $"Inspection {inspection.Id} has no result {string.Join(", ", unknown)}."),
            ]);

        var removedRequired = inspection.Results
            .Where(r => r.IsRequired && !keptIds.Contains(r.Id))
            .Select(r => r.Description)
            .ToList();
        if (removedRequired.Count > 0)
            throw new ValidationException(
            [
                new ValidationFailure(
                    nameof(UpdateQcInspectionRequestModel.Results),
                    $"Required checklist items cannot be removed ({string.Join(", ", removedRequired)})."),
            ]);

        var changed = false;
        foreach (var stale in inspection.Results.Where(r => !keptIds.Contains(r.Id)).ToList())
        {
            inspection.Results.Remove(stale);
            db.QcInspectionResults.Remove(stale);
            changed = true;
        }

        foreach (var model in incoming)
        {
            var row = model.Id is int id ? inspection.Results.First(r => r.Id == id) : null;
            if (row is null)
            {
                row = new QcInspectionResult
                {
                    InspectionId = inspection.Id,
                    ChecklistItemId = model.ChecklistItemId,
                    IsRequired = false,
                };
                inspection.Results.Add(row);
                changed = true;
            }

            var description = model.Description.Trim();
            var measuredValue = model.MeasuredValue?.Trim();
            var notes = model.Notes?.Trim();
            if (row.Description == description && row.Passed == model.Passed
                && row.MeasuredValue == measuredValue && row.Notes == notes)
                continue;

            row.Description = description;
            row.Passed = model.Passed;
            row.MeasuredValue = measuredValue;
            row.Notes = notes;
            changed = true;
        }

        return changed;
    }

    private static void EnsureRequiredItemsChecked(QcInspection inspection)
    {
        var notChecked = inspection.Results
            .Where(r => r.IsRequired && r.Passed is null)
            .OrderBy(r => r.Id)
            .Select(r => r.Description)
            .ToList();

        if (notChecked.Count > 0)
            throw new InvalidOperationException(
                $"Inspection {inspection.Id} cannot be completed: required checklist items have not been checked ({string.Join(", ", notChecked)}).");
    }

    private static void EnsureRequiredItemsPassed(QcInspection inspection)
    {
        var failed = inspection.Results
            .Where(r => r.IsRequired && r.Passed == false)
            .OrderBy(r => r.Id)
            .Select(r => r.Description)
            .ToList();

        if (failed.Count > 0)
            throw new InvalidOperationException(
                $"Inspection {inspection.Id} cannot pass: required checklist items did not pass ({string.Join(", ", failed)}).");
    }
}
