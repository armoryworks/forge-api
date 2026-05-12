using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

/// <summary>
/// Paged list of receiving-inspection templates for the
/// <c>&lt;app-entity-picker entityType="receiving-inspection-templates"&gt;</c>
/// shared component on the Part Quality cluster. Backed by
/// <see cref="Forge.Core.Entities.QcChecklistTemplate"/> — receiving
/// inspections reuse the QC checklist template structure.
///
/// Mirrors the <see cref="Forge.Api.Features.Parts.GetPartsQuery"/>
/// search + pagination contract: <c>?search=…&amp;page=1&amp;pageSize=20</c>.
/// Default page size 20, capped at 100.
/// </summary>
public record GetReceivingInspectionTemplatesQuery(
    string? Search,
    int Page,
    int PageSize) : IRequest<PagedResponse<ReceivingInspectionTemplateListItemModel>>;

public class GetReceivingInspectionTemplatesHandler(AppDbContext db)
    : IRequestHandler<GetReceivingInspectionTemplatesQuery, PagedResponse<ReceivingInspectionTemplateListItemModel>>
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public async Task<PagedResponse<ReceivingInspectionTemplateListItemModel>> Handle(
        GetReceivingInspectionTemplatesQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0
            ? DefaultPageSize
            : Math.Min(request.PageSize, MaxPageSize);

        var query = db.QcChecklistTemplates
            .AsNoTracking()
            .Where(t => t.IsActive);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(t =>
                t.Name.ToLower().Contains(term)
                || (t.Description != null && t.Description.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(t => t.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new ReceivingInspectionTemplateListItemModel(
                t.Id, t.Name, t.Description, t.IsActive))
            .ToListAsync(cancellationToken);

        return new PagedResponse<ReceivingInspectionTemplateListItemModel>(
            items, totalCount, page, pageSize);
    }
}
