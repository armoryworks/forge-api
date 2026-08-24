using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Enums;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Sales-tax liability report — the seller sales-tax the business owes,
/// aggregated by jurisdiction (ship-to US state) over an optional date range.
/// Read-only aggregate over <see cref="Forge.Core.Entities.SalesOrder"/>; no
/// mutation, so no <c>ActivityLog</c>.
///
/// <para><b>Correctness invariant (Sales Channels &amp; the Retail Lane).</b> The
/// liability sums <see cref="Forge.Core.Entities.SalesOrder.SellerTaxLiability"/>,
/// never <c>TaxAmount</c>: marketplace-facilitator orders are collected and
/// remitted by the platform, so their tax is never the seller's payable and must
/// not inflate what the report says the business owes.</para>
///
/// <para>Gated on <c>CAP-ACCT-GL-VIEW</c> like the sibling aging reports on
/// <see cref="Forge.Api.Controllers.AccountingGlController"/>.</para>
/// </summary>
[RequiresCapability("CAP-ACCT-GL-VIEW")]
public record GetSalesTaxLiabilityQuery(DateOnly? FromDate = null, DateOnly? ToDate = null)
    : IRequest<SalesTaxLiabilityReport>;

public class GetSalesTaxLiabilityHandler(AppDbContext db)
    : IRequestHandler<GetSalesTaxLiabilityQuery, SalesTaxLiabilityReport>
{
    public async Task<SalesTaxLiabilityReport> Handle(
        GetSalesTaxLiabilityQuery request, CancellationToken cancellationToken)
    {
        DateTimeOffset? fromBound = request.FromDate is { } f
            ? new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;
        DateTimeOffset? toBound = request.ToDate is { } t
            ? new DateTimeOffset(t.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero)
            : null;

        // Draft orders aren't yet real sales and Cancelled orders never shipped —
        // neither carries a liability. Order date = ConfirmedDate when set, else
        // the row's creation date.
        var query = db.SalesOrders.AsNoTracking()
            .Where(o => o.Status != SalesOrderStatus.Draft && o.Status != SalesOrderStatus.Cancelled);

        if (fromBound is not null)
            query = query.Where(o => (o.ConfirmedDate ?? o.CreatedAt) >= fromBound);
        if (toBound is not null)
            query = query.Where(o => (o.ConfirmedDate ?? o.CreatedAt) <= toBound);

        // Project only the scalars the liability needs — subtotal is a translatable
        // correlated Σ(qty × unit price), not the NotMapped SalesOrder.Subtotal.
        // Jurisdiction = ship-to state: the customer shipping address, else the
        // frozen retail ship-to (SalesOrder.ShippingAddress ?? SalesOrder.ShipTo).
        var orders = await query
            .Select(o => new OrderTaxRow(
                o.ShippingAddress != null ? o.ShippingAddress.State
                    : (o.ShipTo != null ? o.ShipTo.State : null),
                o.TaxRate,
                o.TaxCollectedBy,
                o.Lines.Sum(l => l.Quantity * l.UnitPrice)))
            .ToListAsync(cancellationToken);

        var rows = new List<SalesTaxLiabilityJurisdictionRow>();

        foreach (var group in orders.GroupBy(NormalizeJurisdiction))
        {
            decimal taxableBase = 0m;
            decimal liability = 0m;
            var orderCount = 0;

            foreach (var order in group)
            {
                orderCount++;

                // SellerTaxLiability: marketplace-collected tax is remitted by the
                // platform, never the seller's payable — it contributes zero.
                if (order.TaxCollectedBy == TaxCollectedBy.Marketplace)
                    continue;

                taxableBase += order.Subtotal;
                liability += order.Subtotal * order.TaxRate;
            }

            rows.Add(new SalesTaxLiabilityJurisdictionRow
            {
                Jurisdiction = group.Key,
                TaxableBase = taxableBase,
                Liability = liability,
                OrderCount = orderCount,
            });
        }

        return new SalesTaxLiabilityReport
        {
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            Jurisdictions = rows
                .OrderByDescending(r => r.Liability)
                .ThenBy(r => r.Jurisdiction)
                .ToList(),
            TotalTaxableBase = rows.Sum(r => r.TaxableBase),
            TotalLiability = rows.Sum(r => r.Liability),
            TotalOrderCount = rows.Sum(r => r.OrderCount),
        };
    }

    private static string? NormalizeJurisdiction(OrderTaxRow row) =>
        string.IsNullOrWhiteSpace(row.State) ? null : row.State.Trim().ToUpperInvariant();

    private sealed record OrderTaxRow(string? State, decimal TaxRate, TaxCollectedBy TaxCollectedBy, decimal Subtotal);
}
