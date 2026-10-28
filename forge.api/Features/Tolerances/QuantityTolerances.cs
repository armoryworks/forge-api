using System.Globalization;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;

namespace Forge.Api.Features.Tolerances;

/// <summary>
/// Resolves the over/under quantity tolerance for a line: the line's own value, else its vendor's or
/// customer's, else the company default from <see cref="ToleranceSettings"/>. Each percentage resolves on its
/// own, so a line can override only one of them. Without a database (mock-based handler tests) the tolerance
/// is 0/0, the exact-quantity behaviour.
/// </summary>
public static class QuantityTolerances
{
    public static QuantityTolerance Resolve(
        decimal? lineOver, decimal? lineUnder, decimal? partyOver, decimal? partyUnder, QuantityTolerance defaults)
        => new(
            Clamp(lineOver ?? partyOver ?? defaults.OverPercent, ToleranceSettings.MaxOverPercent),
            Clamp(lineUnder ?? partyUnder ?? defaults.UnderPercent, ToleranceSettings.MaxUnderPercent));

    public static Task<QuantityTolerance> PurchaseDefaultsAsync(AppDbContext db, CancellationToken ct)
        => DefaultsAsync(db, ToleranceSettings.OverReceiptPercentKey, ToleranceSettings.UnderReceiptPercentKey, ct);

    public static Task<QuantityTolerance> SalesDefaultsAsync(AppDbContext db, CancellationToken ct)
        => DefaultsAsync(db, ToleranceSettings.OverShipPercentKey, ToleranceSettings.UnderShipPercentKey, ct);

    public static async Task<QuantityTolerance> ForPurchaseLineAsync(
        AppDbContext? db, PurchaseOrderLine line, int vendorId, CancellationToken ct)
        => (await ForVendorAsync(db, vendorId, ct))(line);

    public static async Task<Func<PurchaseOrderLine, QuantityTolerance>> ForVendorAsync(
        AppDbContext? db, int vendorId, CancellationToken ct)
    {
        if (db is null)
            return line => Resolve(line.OverReceiptTolerancePct, line.UnderReceiptTolerancePct, null, null, QuantityTolerance.None);

        var vendor = await db.Vendors.AsNoTracking()
            .Where(v => v.Id == vendorId)
            .Select(v => new { v.OverReceiptTolerancePct, v.UnderReceiptTolerancePct })
            .FirstOrDefaultAsync(ct);
        var defaults = await PurchaseDefaultsAsync(db, ct);
        return line => Resolve(line.OverReceiptTolerancePct, line.UnderReceiptTolerancePct,
            vendor?.OverReceiptTolerancePct, vendor?.UnderReceiptTolerancePct, defaults);
    }

    public static async Task<Func<SalesOrderLine, QuantityTolerance>> ForSalesOrderAsync(
        AppDbContext? db, SalesOrder order, CancellationToken ct)
    {
        if (db is null)
            return line => Resolve(line.OverShipTolerancePct, line.UnderShipTolerancePct, null, null, QuantityTolerance.None);

        var customer = await db.Customers.AsNoTracking()
            .Where(c => c.Id == order.CustomerId)
            .Select(c => new { c.OverShipTolerancePct, c.UnderShipTolerancePct })
            .FirstOrDefaultAsync(ct);
        var defaults = await SalesDefaultsAsync(db, ct);
        return line => Resolve(line.OverShipTolerancePct, line.UnderShipTolerancePct,
            customer?.OverShipTolerancePct, customer?.UnderShipTolerancePct, defaults);
    }

    public static decimal? ParsePercent(string? raw)
        => decimal.TryParse(raw?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value >= 0m
            ? value
            : null;

    private static async Task<QuantityTolerance> DefaultsAsync(AppDbContext db, string overKey, string underKey, CancellationToken ct)
    {
        var rows = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Key == overKey || s.Key == underKey)
            .Select(s => new { s.Key, s.Value })
            .ToListAsync(ct);
        decimal Read(string key) => ParsePercent(rows.FirstOrDefault(r => r.Key == key)?.Value)
            ?? ParsePercent(SettingDescriptorCatalog.FindByKey(key)?.DefaultValue)
            ?? 0m;
        return new QuantityTolerance(Read(overKey), Read(underKey));
    }

    private static decimal Clamp(decimal value, decimal max) => Math.Min(Math.Max(value, 0m), max);
}
