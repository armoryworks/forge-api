using Microsoft.EntityFrameworkCore;

using Forge.Core.Settings;
using Forge.Data.Context;

namespace Forge.Data.Services;

/// <summary>The early-payment discount settings in force, read straight from <c>system_settings</c>.</summary>
public sealed record DiscountSettings(bool BaseIsSubtotal, int GraceDays)
{
    public static readonly DiscountSettings Default = new(false, 0);

    public static async Task<DiscountSettings> ReadAsync(AppDbContext db, CancellationToken ct)
    {
        var values = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Key == PaymentTermsSettings.DiscountBaseKey || s.Key == PaymentTermsSettings.DiscountGraceDaysKey)
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        var subtotal = string.Equals(
            values.GetValueOrDefault(PaymentTermsSettings.DiscountBaseKey)?.Trim(),
            PaymentTermsSettings.BaseSubtotal,
            StringComparison.OrdinalIgnoreCase);
        var grace = int.TryParse(values.GetValueOrDefault(PaymentTermsSettings.DiscountGraceDaysKey), out var days) && days > 0
            ? days
            : 0;
        return new DiscountSettings(subtotal, grace);
    }
}
