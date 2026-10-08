using System.Security.Claims;

using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Quality;

public record CreateQcInspectionCommand(CreateQcInspectionRequestModel Data) : IRequest<QcInspectionResponseModel>;

public class CreateQcInspectionCommandValidator : AbstractValidator<CreateQcInspectionCommand>
{
    public CreateQcInspectionCommandValidator()
    {
        RuleFor(x => x.Data.LotNumber).MaximumLength(100).When(x => x.Data.LotNumber is not null);
        RuleFor(x => x.Data.Notes).MaximumLength(2000).When(x => x.Data.Notes is not null);
    }
}

public class CreateQcInspectionHandler(AppDbContext db, IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<CreateQcInspectionCommand, QcInspectionResponseModel>
{
    public async Task<QcInspectionResponseModel> Handle(
        CreateQcInspectionCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        var userId = int.Parse(httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var jobId = PositiveOrNull(data.JobId);
        var productionRunId = PositiveOrNull(data.ProductionRunId);
        var templateId = PositiveOrNull(data.TemplateId);
        var partId = PositiveOrNull(data.PartId);

        var failures = new List<ValidationFailure>();

        if (jobId is int job)
        {
            var jobPart = await db.Jobs
                .AsNoTracking()
                .Where(j => j.Id == job)
                .Select(j => new { j.PartId })
                .FirstOrDefaultAsync(cancellationToken);
            if (jobPart is null)
                failures.Add(new ValidationFailure("jobId", "Pick a work order that exists.") { AttemptedValue = job });
            else
                partId ??= jobPart.PartId;
        }

        if (productionRunId is int run && !await db.ProductionRuns.AnyAsync(r => r.Id == run, cancellationToken))
            failures.Add(new ValidationFailure("productionRunId", "Pick a production run that exists.") { AttemptedValue = run });

        if (templateId is int template
            && !await db.QcChecklistTemplates.AnyAsync(t => t.Id == template && t.IsActive, cancellationToken))
            failures.Add(new ValidationFailure("templateId", "Pick a checklist template that exists.") { AttemptedValue = template });

        if (PositiveOrNull(data.PartId) is int part && !await db.Parts.AnyAsync(p => p.Id == part, cancellationToken))
            failures.Add(new ValidationFailure("partId", "Pick a part that exists.") { AttemptedValue = part });

        if (failures.Count > 0)
            throw new ValidationException(failures);

        var inspection = new QcInspection
        {
            JobId = jobId,
            ProductionRunId = productionRunId,
            TemplateId = templateId,
            PartId = partId,
            InspectorId = userId,
            LotNumber = data.LotNumber?.Trim(),
            Status = "InProgress",
            Notes = data.Notes?.Trim(),
        };

        if (templateId.HasValue)
        {
            var templateItems = await db.QcChecklistItems
                .AsNoTracking()
                .Where(i => i.TemplateId == templateId.Value)
                .OrderBy(i => i.SortOrder)
                .ThenBy(i => i.Id)
                .ToListAsync(cancellationToken);

            inspection.Results = templateItems.Select(item => new QcInspectionResult
            {
                ChecklistItemId = item.Id,
                Description = item.Description,
                Specification = item.Specification,
                IsRequired = item.IsRequired,
            }).ToList();
        }

        db.QcInspections.Add(inspection);
        await db.SaveChangesAsync(cancellationToken);

        var points = new List<(string, int)> { ("QcInspection", inspection.Id) };
        if (inspection.JobId is int loggedJobId)
            points.Add(("Job", loggedJobId));
        db.LogActivityAt("inspection-started", $"Inspection QC #{inspection.Id} started", [.. points]);
        await db.SaveChangesAsync(cancellationToken);

        return await QcInspectionMapping.LoadResponseAsync(db, inspection.Id, cancellationToken);
    }

    private static int? PositiveOrNull(int? id) => id is > 0 ? id : null;
}
