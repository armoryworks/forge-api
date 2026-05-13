using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.TariffRates;

/// <summary>
/// Bought-parts effort PR4 — admin TariffRate list. Returns every row
/// (active + closed) ordered by HTS / country / EffectiveFrom DESC so the
/// admin sees the most-recent rate per (HtsCode, CountryOfOrigin) at the
/// top of each natural group. Soft-deleted rows are filtered out by the
/// global query filter on <see cref="Forge.Platform.Entities.BaseEntity"/>.
/// </summary>
public record GetTariffRatesQuery() : IRequest<List<TariffRateResponseModel>>;

public class GetTariffRatesHandler(AppDbContext db)
    : IRequestHandler<GetTariffRatesQuery, List<TariffRateResponseModel>>
{
    public async Task<List<TariffRateResponseModel>> Handle(GetTariffRatesQuery request, CancellationToken ct)
    {
        return await db.TariffRates
            .AsNoTracking()
            .OrderBy(t => t.HtsCode).ThenBy(t => t.CountryOfOrigin).ThenByDescending(t => t.EffectiveFrom)
            .Select(t => new TariffRateResponseModel(
                t.Id, t.HtsCode, t.CountryOfOrigin, t.RatePct,
                t.EffectiveFrom, t.EffectiveTo, t.Source, t.CreatedAt, t.UpdatedAt))
            .ToListAsync(ct);
    }
}
