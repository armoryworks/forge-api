using Microsoft.EntityFrameworkCore;

using Forge.Core.Constants;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipments;

/// <summary>
/// Customs data for a cross-border shipment: each line's HTS code and country of origin, printed on
/// the packing slip, the bill of lading and the ship document when the ship-to country differs from
/// the ship-from country (the default company location).
///
/// <para>HTS code: the part's own code, else its preferred vendor part's. Country of origin: the
/// preferred vendor part's for a bought part; the ship-from country for a part made in house; else
/// any vendor part's that names one. Unknown values print as "—" so a gap is visible rather than
/// silently omitted.</para>
/// </summary>
public sealed class ShipmentCustoms
{
    private readonly IReadOnlyDictionary<int, ShipmentCustomsLine> _lines;

    private ShipmentCustoms(string shipFrom, string shipTo, IReadOnlyDictionary<int, ShipmentCustomsLine> lines)
    {
        ShipFromCountry = shipFrom;
        ShipToCountry = shipTo;
        _lines = lines;
    }

    public string ShipFromCountry { get; }

    public string ShipToCountry { get; }

    /// <summary>The customs data for a shipment line (blank values when its part is unknown).</summary>
    public ShipmentCustomsLine For(ShipmentLine line) =>
        _lines.TryGetValue(line.Id, out var info) ? info : new ShipmentCustomsLine(null, null);

    /// <summary>Formats a customs value for print.</summary>
    public static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

    /// <summary>
    /// Loads the customs data for <paramref name="shipment"/>, or null when the shipment does not cross a
    /// border (or either country is unknown). Reads the ship-to address and the parts itself, so callers
    /// need not include them.
    /// </summary>
    public static async Task<ShipmentCustoms?> LoadAsync(AppDbContext db, Shipment shipment, CancellationToken ct)
    {
        var shipFrom = await db.CompanyLocations.AsNoTracking()
            .Where(l => l.IsActive)
            .OrderByDescending(l => l.IsDefault)
            .ThenBy(l => l.Id)
            .Select(l => l.Country)
            .FirstOrDefaultAsync(ct);

        var shipTo = await ShipToCountryAsync(db, shipment, ct);
        if (!CountryCodes.AreDifferent(shipFrom, shipTo))
            return null;

        var partByLine = shipment.Lines.ToDictionary(
            l => l.Id,
            l => l.PartId ?? l.SalesOrderLine?.PartId);
        var partIds = partByLine.Values.OfType<int>().Distinct().ToList();

        var parts = await db.Parts.AsNoTracking()
            .Where(p => partIds.Contains(p.Id))
            .Select(p => new { p.Id, p.HtsCode, p.ProcurementSource })
            .ToDictionaryAsync(p => p.Id, ct);
        var vendorParts = (await db.VendorParts.AsNoTracking()
                .Where(vp => partIds.Contains(vp.PartId))
                .Select(vp => new { vp.PartId, vp.IsPreferred, vp.Id, vp.HtsCode, vp.CountryOfOrigin })
                .ToListAsync(ct))
            .GroupBy(vp => vp.PartId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(vp => vp.IsPreferred).ThenBy(vp => vp.Id).ToList());

        var lines = new Dictionary<int, ShipmentCustomsLine>();
        foreach (var (lineId, partId) in partByLine)
        {
            if (partId is not int id || !parts.TryGetValue(id, out var part))
                continue;

            var sources = vendorParts.GetValueOrDefault(id) ?? [];
            var preferred = sources.FirstOrDefault();
            var hts = !string.IsNullOrWhiteSpace(part.HtsCode)
                ? part.HtsCode
                : sources.Select(vp => vp.HtsCode).FirstOrDefault(h => !string.IsNullOrWhiteSpace(h));
            var origin = part.ProcurementSource == ProcurementSource.Make
                ? CountryCodes.Normalize(shipFrom)
                : preferred?.CountryOfOrigin
                    ?? sources.Select(vp => vp.CountryOfOrigin).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
            lines[lineId] = new ShipmentCustomsLine(hts, origin);
        }

        return new ShipmentCustoms(CountryCodes.Normalize(shipFrom), CountryCodes.Normalize(shipTo), lines);
    }

    private static async Task<string?> ShipToCountryAsync(AppDbContext db, Shipment shipment, CancellationToken ct)
    {
        if (shipment.ShippingAddress is { } address)
            return address.Country;
        if (shipment.ShippingAddressId is int addressId)
            return await db.CustomerAddresses.AsNoTracking()
                .Where(a => a.Id == addressId).Select(a => a.Country).FirstOrDefaultAsync(ct);

        return await db.SalesOrders.AsNoTracking()
            .Where(o => o.Id == shipment.SalesOrderId)
            .Select(o => o.ShippingAddress != null ? o.ShippingAddress.Country
                : o.ShipTo != null ? o.ShipTo.Country
                : null)
            .FirstOrDefaultAsync(ct);
    }
}
