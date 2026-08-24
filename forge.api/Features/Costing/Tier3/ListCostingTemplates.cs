using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Models.Costing;
using Forge.Data.Context;

namespace Forge.Api.Features.Costing.Tier3;

/// <summary>Lists costing templates (system first, then by name) with their lines.</summary>
[RequiresCapability("CAP-COSTING-TIER3-ABC")]
public record ListCostingTemplatesQuery : IRequest<IReadOnlyList<CostingTemplateResponseModel>>;

public class ListCostingTemplatesHandler(AppDbContext db)
    : IRequestHandler<ListCostingTemplatesQuery, IReadOnlyList<CostingTemplateResponseModel>>
{
    public async Task<IReadOnlyList<CostingTemplateResponseModel>> Handle(
        ListCostingTemplatesQuery request, CancellationToken ct)
    {
        var templates = await db.CostingTemplates.AsNoTracking()
            .Include(t => t.Lines)
            .OrderByDescending(t => t.IsSystem)
            .ThenBy(t => t.Name)
            .ToListAsync(ct);

        return templates.Select(t => new CostingTemplateResponseModel(
            t.Id, t.Name, t.Description, t.IsSystem,
            t.Lines.OrderBy(l => l.SortOrder).Select(l => new CostingTemplateLineModel(
                l.Id, l.Code, l.Name, l.Behavior, l.Driver, l.AmountBasis,
                l.DefaultValue, l.GlAccountNumber, l.GlAccountName, l.SortOrder)).ToList()))
            .ToList();
    }
}
