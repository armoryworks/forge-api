using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Serilog;

namespace Forge.Api.Data;

public static partial class SeedData
{
    /// <summary>
    /// The seeded payment terms, matching forge-db <c>data/0060-payment-terms.sql</c>: the legacy Net-N credit
    /// terms one to one, COD, and the common early-payment discount terms.
    /// </summary>
    internal static readonly (string Code, string Name, int NetDays, decimal DiscountPercent, int DiscountDays, CreditTerms? Legacy, int SortOrder)[] SystemPaymentTerms =
    [
        ("DueOnReceipt", "Due on receipt", 0, 0m, 0, CreditTerms.DueOnReceipt, 10),
        ("COD", "COD", 0, 0m, 0, null, 20),
        ("Net15", "Net 15", 15, 0m, 0, CreditTerms.Net15, 30),
        ("Net30", "Net 30", 30, 0m, 0, CreditTerms.Net30, 40),
        ("Net45", "Net 45", 45, 0m, 0, CreditTerms.Net45, 50),
        ("Net60", "Net 60", 60, 0m, 0, CreditTerms.Net60, 60),
        ("Net90", "Net 90", 90, 0m, 0, CreditTerms.Net90, 70),
        ("1/10Net30", "1/10 Net 30", 30, 1m, 10, null, 80),
        ("2/10Net30", "2/10 Net 30", 30, 2m, 10, null, 90),
        ("2/10Net60", "2/10 Net 60", 60, 2m, 10, null, 100),
    ];

    /// <summary>Adds any seeded payment terms the install does not have yet (by code). Runs every boot; tenant edits are kept.</summary>
    internal static async Task EnsurePaymentTermsAsync(AppDbContext db)
    {
        var existing = await db.PaymentTerms.IgnoreQueryFilters()
            .Select(t => new { t.Code, t.LegacyCreditTerms })
            .ToListAsync();
        var codes = existing.Select(t => t.Code).ToHashSet(StringComparer.Ordinal);
        var legacyTaken = existing.Where(t => t.LegacyCreditTerms != null).Select(t => t.LegacyCreditTerms!.Value).ToHashSet();

        var added = 0;
        foreach (var t in SystemPaymentTerms.Where(t => !codes.Contains(t.Code)))
        {
            db.PaymentTerms.Add(new PaymentTerm
            {
                Code = t.Code,
                Name = t.Name,
                NetDays = t.NetDays,
                DiscountPercent = t.DiscountPercent,
                DiscountDays = t.DiscountDays,
                LegacyCreditTerms = t.Legacy is CreditTerms legacy && !legacyTaken.Contains(legacy) ? legacy : null,
                IsActive = true,
                SortOrder = t.SortOrder,
                IsSystem = true,
            });
            added++;
        }

        if (added == 0)
            return;

        await db.SaveChangesAsync();
        Log.Information("Seeded {Count} payment terms.", added);
    }
}
