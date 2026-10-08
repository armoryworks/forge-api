using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.VendorParts;

/// <summary>
/// Keeps <see cref="Part.PreferredVendorId"/> and the part's
/// <see cref="VendorPart.IsPreferred"/> flags pointing at the same vendor.
/// Every write stays on the caller's tracked context, so the caller's single
/// SaveChanges commits the part, its vendor sources and the
/// <c>preferred-vendor-changed</c> activity rows together.
/// </summary>
public static class PreferredVendorSync
{
    public const string ActivityAction = "preferred-vendor-changed";

    /// <summary>
    /// Points the part at <paramref name="vendorId"/> (null clears it), marks that
    /// vendor's source preferred and clears the part's other sources. No-op when
    /// the part already points at that vendor.
    /// </summary>
    public static async Task SetPartPreferredVendorAsync(
        AppDbContext db, Part part, int? vendorId, CancellationToken ct)
    {
        if (part.PreferredVendorId == vendorId)
            return;

        var previousVendorId = part.PreferredVendorId;
        part.PreferredVendorId = vendorId;

        var sources = await db.VendorParts
            .Where(vp => vp.PartId == part.Id)
            .ToListAsync(ct);
        foreach (var source in sources)
            source.IsPreferred = vendorId.HasValue && source.VendorId == vendorId.Value;

        await LogChangeAsync(db, part.Id, previousVendorId, vendorId, ct);
    }

    /// <summary>
    /// Applies a vendor source's preferred flag to its part after the flag changed:
    /// a preferred source clears its siblings and becomes the part's preferred
    /// vendor; an un-preferred source clears the part's preferred vendor when it
    /// was the one the part pointed at.
    /// </summary>
    public static async Task ApplyVendorPartPreferenceAsync(
        AppDbContext db, VendorPart vendorPart, CancellationToken ct)
    {
        if (vendorPart.IsPreferred)
        {
            var siblings = await db.VendorParts
                .Where(other => other.PartId == vendorPart.PartId
                    && other.Id != vendorPart.Id
                    && other.IsPreferred)
                .ToListAsync(ct);
            foreach (var sibling in siblings)
                sibling.IsPreferred = false;
        }

        var part = await db.Parts.FirstOrDefaultAsync(p => p.Id == vendorPart.PartId, ct);
        if (part is null)
            return;

        int? target;
        if (vendorPart.IsPreferred)
            target = vendorPart.VendorId;
        else if (part.PreferredVendorId == vendorPart.VendorId)
            target = null;
        else
            return;

        if (part.PreferredVendorId == target)
            return;

        var previousVendorId = part.PreferredVendorId;
        part.PreferredVendorId = target;
        await LogChangeAsync(db, part.Id, previousVendorId, target, ct);
    }

    private static async Task LogChangeAsync(
        AppDbContext db, int partId, int? previousVendorId, int? newVendorId, CancellationToken ct)
    {
        var vendorIds = new[] { previousVendorId, newVendorId }
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();
        var names = await db.Vendors
            .Where(v => vendorIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.CompanyName, ct);

        string Name(int? id) => id is not int value
            ? "(none)"
            : names.TryGetValue(value, out var name) ? name : "(unknown)";

        var indexingPoints = new List<(string EntityType, int EntityId)> { ("Part", partId) };
        indexingPoints.AddRange(vendorIds.Select(id => ("Vendor", id)));

        db.LogActivityAt(
            ActivityAction,
            $"Preferred vendor changed from {Name(previousVendorId)} to {Name(newVendorId)}",
            [.. indexingPoints]);
    }
}
