using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record GetQcInspectionByIdQuery(int Id) : IRequest<QcInspectionDetailResponseModel>;

public class GetQcInspectionByIdHandler(AppDbContext db)
    : IRequestHandler<GetQcInspectionByIdQuery, QcInspectionDetailResponseModel>
{
    public async Task<QcInspectionDetailResponseModel> Handle(
        GetQcInspectionByIdQuery request, CancellationToken cancellationToken)
    {
        var inspection = await db.QcInspections
            .AsNoTracking()
            .Include(i => i.Results)
            .Include(i => i.Job)
            .Include(i => i.ProductionRun)
            .Include(i => i.Part)
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Inspection {request.Id} not found.");

        var inspectorNames = await QcInspectionMapping.InspectorNamesAsync(db, [inspection.InspectorId], cancellationToken);
        var templateNames = await QcInspectionMapping.TemplateNamesAsync(db, [inspection.TemplateId], cancellationToken);

        return new QcInspectionDetailResponseModel(
            inspection.Id,
            inspection.JobId,
            inspection.Job?.JobNumber,
            inspection.Job?.Title,
            inspection.ProductionRunId,
            inspection.ProductionRun?.RunNumber,
            inspection.TemplateId,
            inspection.TemplateId is int templateId ? templateNames.GetValueOrDefault(templateId) : null,
            inspection.PartId,
            inspection.Part?.PartNumber,
            inspection.Part?.Name,
            inspection.InspectorId,
            inspectorNames.GetValueOrDefault(inspection.InspectorId, string.Empty),
            inspection.LotNumber,
            inspection.Status,
            inspection.Notes,
            inspection.CompletedAt,
            inspection.Results.OrderBy(r => r.Id).Select(QcInspectionMapping.ToResultModel).ToList(),
            inspection.CreatedAt,
            inspection.UpdatedAt);
    }
}
