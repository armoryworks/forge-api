using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.VendorParts;

/// <summary>
/// Pillar 3 — Upsert a tiered-price row on a VendorPart with SCD Type 2
/// supersede semantics. See <c>docs/vendor-tier-pricing-history-2026-05-04.md</c>.
///
/// <para>If a currently-effective tier exists at this <c>MinQuantity</c>,
/// the handler stamps its <c>EffectiveTo</c> to today and INSERTS a new
/// row with the supplied values. Both rows persist; PO line items pointing
/// at the superseded tier id keep working. If no currently-effective tier
/// exists at the given min_qty, the handler simply inserts a new row.</para>
///
/// <para><c>Currency</c> snapshots from <see cref="VendorPart.Currency"/>
/// at insert time so a later currency change on the source doesn't
/// retroactively rewrite history.</para>
/// </summary>
public record UpsertVendorPartPriceTierCommand(int VendorPartId, UpsertVendorPartPriceTierRequestModel Body)
    : IRequest<VendorPartPriceTierResponseModel>;

public class UpsertVendorPartPriceTierValidator : AbstractValidator<UpsertVendorPartPriceTierCommand>
{
    public UpsertVendorPartPriceTierValidator()
    {
        RuleFor(x => x.VendorPartId).GreaterThan(0);
        RuleFor(x => x.Body.MinQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Body.UnitPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Body.Notes).MaximumLength(2000);
        RuleFor(x => x.Body)
            .Must(b => !b.EffectiveTo.HasValue || !b.EffectiveFrom.HasValue
                || b.EffectiveTo.Value >= b.EffectiveFrom.Value)
            .WithMessage("EffectiveTo must be on or after EffectiveFrom.");
    }
}

public class UpsertVendorPartPriceTierHandler(AppDbContext db, IClock clock)
    : IRequestHandler<UpsertVendorPartPriceTierCommand, VendorPartPriceTierResponseModel>
{
    public async Task<VendorPartPriceTierResponseModel> Handle(UpsertVendorPartPriceTierCommand request, CancellationToken ct)
    {
        var vp = await db.VendorParts
            .Include(x => x.Vendor)
            .FirstOrDefaultAsync(x => x.Id == request.VendorPartId, ct)
            ?? throw new KeyNotFoundException($"VendorPart {request.VendorPartId} not found");

        var body = request.Body;
        var now = clock.UtcNow;
        var effectiveFrom = body.EffectiveFrom ?? now;

        // Find a currently-effective tier at this min_qty (the supersede target).
        // "Currently effective" means: effective_from <= now AND
        // (effective_to IS NULL OR effective_to >= now). The new row's
        // effective_from also must overlap — we treat any unclosed row at
        // this min_qty as the supersede target.
        var existing = await db.VendorPartPriceTiers
            .Where(t => t.VendorPartId == request.VendorPartId
                && t.MinQuantity == body.MinQuantity
                && t.EffectiveTo == null)
            .OrderByDescending(t => t.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

        VendorPartPriceTier newTier;
        VendorPartPriceTier? superseded = null;

        if (existing is not null)
        {
            // Idempotency / no-op guard: if the supplied values exactly match
            // the existing row, return the existing without superseding. Avoids
            // an empty close-and-reopen on every PUT replay.
            if (existing.UnitPrice == body.UnitPrice
                && existing.Notes == body.Notes
                && existing.EffectiveFrom == effectiveFrom)
            {
                return VendorPartMapper.ToTierResponse(existing);
            }

            // Supersede: close out the existing row, insert the new one.
            existing.EffectiveTo = now;
            superseded = existing;
        }

        newTier = new VendorPartPriceTier
        {
            VendorPartId = request.VendorPartId,
            MinQuantity = body.MinQuantity,
            UnitPrice = body.UnitPrice,
            // Snapshot currency from parent so historical rows preserve what
            // they were quoted at if the source's currency later changes.
            Currency = vp.Currency,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = body.EffectiveTo,
            Notes = body.Notes,
        };
        db.VendorPartPriceTiers.Add(newTier);

        // Indexing-points + rollup rule (CLAUDE.md): a tier supersede is ONE
        // activity row that summarizes both old and new values, logged on
        // BOTH Part and Vendor.
        var vendorName = vp.Vendor?.CompanyName ?? "(unknown vendor)";
        var summary = superseded is null
            ? $"Price tier added for {vendorName}: qty ≥ {newTier.MinQuantity} @ {newTier.UnitPrice:0.##} {newTier.Currency} (effective {newTier.EffectiveFrom:yyyy-MM-dd})"
            : $"Price tier superseded for {vendorName}: qty ≥ {newTier.MinQuantity} {superseded.UnitPrice:0.##} {superseded.Currency} → {newTier.UnitPrice:0.##} {newTier.Currency} (new effective {newTier.EffectiveFrom:yyyy-MM-dd})";
        db.LogActivityAt(
            superseded is null ? "price-tier-added" : "price-tier-superseded",
            summary,
            ("Part", vp.PartId),
            ("Vendor", vp.VendorId));

        await db.SaveChangesAsync(ct);

        return VendorPartMapper.ToTierResponse(newTier);
    }
}
