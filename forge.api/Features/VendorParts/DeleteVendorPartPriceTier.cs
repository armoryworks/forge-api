using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.VendorParts;

/// <summary>
/// Pillar 3 — Soft-close a price tier row (SCD Type 2). Stamps
/// <c>EffectiveTo</c> with the current clock value rather than physically
/// deleting; preserves history for PO line items that reference the tier
/// id and for the "Show history" toggle in the UI.
///
/// <para>Idempotent: re-deleting an already-closed tier is a no-op (returns
/// without an additional activity log entry).</para>
/// </summary>
public record DeleteVendorPartPriceTierCommand(int VendorPartId, int TierId) : IRequest;

public class DeleteVendorPartPriceTierHandler(AppDbContext db, IClock clock)
    : IRequestHandler<DeleteVendorPartPriceTierCommand>
{
    public async Task Handle(DeleteVendorPartPriceTierCommand request, CancellationToken ct)
    {
        var tier = await db.VendorPartPriceTiers
            .Include(t => t.VendorPart)
                .ThenInclude(vp => vp.Vendor)
            .FirstOrDefaultAsync(t => t.Id == request.TierId && t.VendorPartId == request.VendorPartId, ct)
            ?? throw new KeyNotFoundException(
                $"Price tier {request.TierId} not found on VendorPart {request.VendorPartId}");

        // Idempotent — already closed.
        if (tier.EffectiveTo is not null && tier.EffectiveTo <= clock.UtcNow)
            return;

        tier.EffectiveTo = clock.UtcNow;

        var vp = tier.VendorPart;
        var vendorName = vp.Vendor?.CompanyName ?? "(unknown vendor)";
        var summary = $"Price tier removed for {vendorName}: qty ≥ {tier.MinQuantity} @ {tier.UnitPrice:0.##} {tier.Currency}";

        // Indexing-points rule: tiers bridge Part ↔ Vendor — log on both.
        db.LogActivityAt(
            "price-tier-removed",
            summary,
            ("Part", vp.PartId),
            ("Vendor", vp.VendorId));

        await db.SaveChangesAsync(ct);
    }
}
