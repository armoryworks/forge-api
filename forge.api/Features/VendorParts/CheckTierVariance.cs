using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.VendorParts;

/// <summary>
/// Bought-parts effort PR4 — variance check for the off-tier prompt at PO
/// line entry. Resolves each (vendorId, partId, qty) tuple to its current
/// effective <see cref="Forge.Core.Entities.VendorPartPriceTier"/>,
/// computes the absolute variance against the entered <c>UnitPrice</c>,
/// and flags lines where the variance exceeds the configured threshold.
/// </summary>
public record CheckTierVarianceQuery(int VendorId, List<CheckTierVarianceLineModel> Lines)
    : IRequest<CheckTierVarianceResponseModel>;

public class CheckTierVarianceHandler(
    AppDbContext db,
    ISystemSettingRepository settingRepo,
    IClock clock)
    : IRequestHandler<CheckTierVarianceQuery, CheckTierVarianceResponseModel>
{
    private const string SystemSettingKey = "purchasing.offTierVariancePct";
    private const decimal FallbackThresholdPct = 5m;

    public async Task<CheckTierVarianceResponseModel> Handle(CheckTierVarianceQuery request, CancellationToken ct)
    {
        // Resolve threshold: per-vendor override → system setting → 5%.
        var vendor = await db.Vendors.AsNoTracking()
            .Where(v => v.Id == request.VendorId)
            .Select(v => new { v.OffTierVariancePct })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Vendor {request.VendorId} not found");

        decimal thresholdPct;
        if (vendor.OffTierVariancePct.HasValue)
        {
            thresholdPct = vendor.OffTierVariancePct.Value;
        }
        else
        {
            var setting = await settingRepo.FindByKeyAsync(SystemSettingKey, ct);
            thresholdPct = decimal.TryParse(setting?.Value, out var parsed) ? parsed : FallbackThresholdPct;
        }

        // Pre-load the relevant VendorParts + tier rows for the requested
        // parts in one round trip. Filter to the active tier window
        // (EffectiveFrom <= now < EffectiveTo or open-ended).
        var partIds = request.Lines.Select(l => l.PartId).Distinct().ToList();
        var now = clock.UtcNow;

        var vendorParts = await db.VendorParts
            .AsNoTracking()
            .Where(vp => vp.VendorId == request.VendorId && partIds.Contains(vp.PartId))
            .Select(vp => new
            {
                vp.Id,
                vp.PartId,
                vp.Currency,
                Tiers = vp.PriceTiers
                    .Where(t => t.EffectiveFrom <= now && (t.EffectiveTo == null || t.EffectiveTo > now))
                    .OrderByDescending(t => t.MinQuantity)
                    .Select(t => new { t.MinQuantity, t.UnitPrice, t.Currency })
                    .ToList(),
            })
            .ToListAsync(ct);

        var vpByPartId = vendorParts.ToDictionary(v => v.PartId);

        var resultLines = new List<CheckTierVarianceResultModel>(request.Lines.Count);
        foreach (var line in request.Lines)
        {
            int? vendorPartId = null;
            decimal? tierPrice = null;
            string? currency = null;
            decimal? variancePct = null;
            bool isOffTier;

            if (vpByPartId.TryGetValue(line.PartId, out var vp))
            {
                vendorPartId = vp.Id;
                currency = vp.Currency;
                // Pick the tier whose MinQuantity is the largest <= request qty.
                var tier = vp.Tiers.FirstOrDefault(t => t.MinQuantity <= line.Quantity);
                if (tier != null)
                {
                    tierPrice = tier.UnitPrice;
                    currency = tier.Currency;
                    if (tier.UnitPrice > 0m)
                    {
                        variancePct = Math.Abs((line.UnitPrice - tier.UnitPrice) / tier.UnitPrice) * 100m;
                        isOffTier = variancePct.Value > thresholdPct;
                    }
                    else
                    {
                        // Tier price is zero (free / placeholder) — any non-zero
                        // entered price is "off-tier" by definition.
                        isOffTier = line.UnitPrice != 0m;
                    }
                }
                else
                {
                    // VendorPart exists but no effective tier — treat as off-tier
                    // so the prompt offers Update Tiers (creates the first tier).
                    isOffTier = true;
                }
            }
            else
            {
                // No VendorPart row yet for (vendor, part) — same disposition.
                isOffTier = true;
            }

            resultLines.Add(new CheckTierVarianceResultModel(
                PartId: line.PartId,
                Quantity: line.Quantity,
                UnitPrice: line.UnitPrice,
                VendorPartId: vendorPartId,
                TierPrice: tierPrice,
                Currency: currency,
                VariancePct: variancePct,
                IsOffTier: isOffTier));
        }

        return new CheckTierVarianceResponseModel(thresholdPct, resultLines);
    }
}
