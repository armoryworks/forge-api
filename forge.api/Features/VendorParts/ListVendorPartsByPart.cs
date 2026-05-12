using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.VendorParts;

/// <summary>
/// Pillar 3 — Catalog of vendors that source a given Part. Powers the
/// part-detail "Sources" tab. Sort order is intentional:
///   1. IsPreferred DESC — the preferred vendor is always at the top.
///   2. IsApproved DESC — approved vendors next, unapproved last.
///   3. VendorCompanyName ASC — alphabetic within each group.
///
/// <para>Each VendorPart's price tiers default to <strong>currently
/// effective only</strong> (effective_from ≤ now AND (effective_to IS
/// NULL OR effective_to ≥ now)). Set <c>ShowHistory = true</c> to return
/// all rows including superseded — drives the "Show history" toggle in
/// the tier table UI.</para>
/// </summary>
public record ListVendorPartsByPartQuery(int PartId, bool ShowHistory = false)
    : IRequest<List<VendorPartResponseModel>>;

public class ListVendorPartsByPartHandler(AppDbContext db, IClock clock)
    : IRequestHandler<ListVendorPartsByPartQuery, List<VendorPartResponseModel>>
{
    public async Task<List<VendorPartResponseModel>> Handle(ListVendorPartsByPartQuery request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var rows = await db.VendorParts
            .Include(x => x.Vendor)
            .Include(x => x.Part)
            .Include(x => x.PriceTiers)
            .Where(x => x.PartId == request.PartId)
            .OrderByDescending(x => x.IsPreferred)
            .ThenByDescending(x => x.IsApproved)
            .ThenBy(x => x.Vendor!.CompanyName)
            .AsNoTracking()
            .ToListAsync(ct);

        if (!request.ShowHistory)
        {
            // Filter price tiers to currently-effective in-memory after the
            // SQL fetch. The set is small (typically <10 tiers per source);
            // keeping the projection in C# keeps the query simple and avoids
            // a SelectMany/GroupBy jump that EF turns into a worse plan.
            foreach (var vp in rows)
            {
                vp.PriceTiers = vp.PriceTiers
                    .Where(t => t.EffectiveFrom <= now
                        && (t.EffectiveTo == null || t.EffectiveTo >= now))
                    .ToList();
            }
        }

        return rows.Select(VendorPartMapper.ToResponse).ToList();
    }
}
