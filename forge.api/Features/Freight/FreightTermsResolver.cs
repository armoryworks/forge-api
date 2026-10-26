using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Freight;

/// <summary>
/// Which freight terms apply to a sales order (its own, else its customer's, else the install default in
/// <see cref="DefaultFreightTermsKey"/>, else <see cref="FreightTerms.PrepaidAndAdd"/>), and what a shipment
/// bills the customer for freight under them.
/// </summary>
public static class FreightTermsResolver
{
    /// <summary>System setting holding the install's default freight terms (a <see cref="FreightTerms"/> name).</summary>
    public const string DefaultFreightTermsKey = "invoices.default_freight_terms";

    /// <summary>The terms used when neither the order, the customer nor the setting names any.</summary>
    public const FreightTerms FallbackTerms = FreightTerms.PrepaidAndAdd;

    public static async Task<FreightTerms> InstallDefaultAsync(AppDbContext db, CancellationToken ct)
    {
        var value = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Key == DefaultFreightTermsKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return Enum.TryParse<FreightTerms>(value, ignoreCase: true, out var terms) && Enum.IsDefined(terms)
            ? terms
            : FallbackTerms;
    }

    public static async Task<FreightTerms> ForSalesOrderAsync(AppDbContext db, int salesOrderId, CancellationToken ct)
    {
        var terms = await db.SalesOrders.AsNoTracking()
            .Where(so => so.Id == salesOrderId)
            .Select(so => so.FreightTerms ?? so.Customer.FreightTerms)
            .FirstOrDefaultAsync(ct);
        return terms ?? await InstallDefaultAsync(db, ct);
    }

    /// <summary>
    /// The freight a shipment bills the customer by default: its carrier cost under
    /// <see cref="FreightTerms.PrepaidAndAdd"/>, 0 under any other terms, null while no carrier cost is known.
    /// </summary>
    public static decimal? DefaultCharge(FreightTerms terms, decimal? shippingCost)
        => shippingCost is not decimal cost ? null
            : terms == FreightTerms.PrepaidAndAdd ? Math.Round(cost, 2, MidpointRounding.AwayFromZero)
            : 0m;
}
