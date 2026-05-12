using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

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

        var inspection = new QcInspection
        {
            JobId = data.JobId,
            ProductionRunId = data.ProductionRunId,
            TemplateId = data.TemplateId,
            InspectorId = userId,
            LotNumber = data.LotNumber?.Trim(),
            Status = "InProgress",
            Notes = data.Notes?.Trim(),
        };

        // If a template is specified, pre-populate results from template items
        if (data.TemplateId.HasValue)
        {
            var templateItems = await db.QcChecklistItems
                .AsNoTracking()
                .Where(i => i.TemplateId == data.TemplateId.Value)
                .OrderBy(i => i.SortOrder)
                .ToListAsync(cancellationToken);

            inspection.Results = templateItems.Select(item => new QcInspectionResult
            {
                ChecklistItemId = item.Id,
                Description = item.Description,
                Passed = false,
            }).ToList();
        }

        db.QcInspections.Add(inspection);
        await db.SaveChangesAsync(cancellationToken);

        return await GetInspectionResponse(inspection.Id, cancellationToken);
    }

    private async Task<QcInspectionResponseModel> GetInspectionResponse(int id, CancellationToken cancellationToken)
    {
        return await db.QcInspections
            .AsNoTracking()
            .Include(i => i.Results)
            .Include(i => i.Job)
            .Include(i => i.Template)
            .Where(i => i.Id == id)
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
