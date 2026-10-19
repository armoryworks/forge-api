using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Sales-tax liability report — the seller sales-tax the business owes,
/// aggregated by jurisdiction (ship-to US state) over an optional date range.
/// Read-only aggregate over finalized <see cref="Invoice"/>s (not Draft, not
/// Voided), dated by invoice date: tax is owed when it is billed, which is also
/// when <see cref="InvoiceArPostingService"/> credits Sales Tax Payable. Each
/// invoice contributes its own <see cref="Invoice.TaxAmount"/> (taxable lines ×
/// the rate stored on the invoice) in functional currency (× the booking
/// <see cref="Invoice.FxRate"/>), so the total ties to that account. No
/// mutation, so no <c>ActivityLog</c>.
///
/// <para><b>Correctness invariant (Sales Channels &amp; the Retail Lane).</b> An
/// invoice for a marketplace-facilitator order
/// (<see cref="SalesOrder.TaxCollectedBy"/> = Marketplace) is counted but
/// contributes zero: the platform collects and remits that tax, so it is never
/// the seller's payable.</para>
///
/// <para>Jurisdiction = the order's ship-to state (customer shipping address,
/// else the frozen retail ship-to), else the customer's default shipping
/// address state for invoices without an order.</para>
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

        var query = db.Invoices.AsNoTracking()
            .Where(i => i.Status != InvoiceStatus.Draft && i.Status != InvoiceStatus.Voided);

        if (fromBound is not null)
            query = query.Where(i => i.InvoiceDate >= fromBound);
        if (toBound is not null)
            query = query.Where(i => i.InvoiceDate <= toBound);

        var invoices = await query
            .Select(i => new InvoiceTaxRow(
                i.SalesOrder != null && i.SalesOrder.ShippingAddress != null ? i.SalesOrder.ShippingAddress.State
                    : i.SalesOrder != null && i.SalesOrder.ShipTo != null ? i.SalesOrder.ShipTo.State
                    : i.Customer.Addresses
                        .Where(a => a.AddressType == AddressType.Shipping || a.AddressType == AddressType.Both)
                        .OrderByDescending(a => a.IsDefault)
                        .ThenBy(a => a.Id)
                        .Select(a => a.State)
                        .FirstOrDefault(),
                i.TaxRate,
                i.FxRate,
                i.SalesOrder != null && i.SalesOrder.TaxCollectedBy == TaxCollectedBy.Marketplace,
                i.Lines.Where(l => l.TaxCode != InvoiceLine.NonTaxableCode).Sum(l => l.Quantity * l.UnitPrice)))
            .ToListAsync(cancellationToken);

        var rows = new List<SalesTaxLiabilityJurisdictionRow>();

        foreach (var group in invoices.GroupBy(NormalizeJurisdiction))
        {
            decimal taxableBase = 0m;
            decimal liability = 0m;
            var invoiceCount = 0;

            foreach (var invoice in group)
            {
                invoiceCount++;

                if (invoice.MarketplaceCollected)
                    continue;

                taxableBase += ToFunctional(invoice.TaxableSubtotal, invoice.FxRate);
                liability += ToFunctional(invoice.TaxableSubtotal * invoice.TaxRate, invoice.FxRate);
            }

            rows.Add(new SalesTaxLiabilityJurisdictionRow
            {
                Jurisdiction = group.Key,
                TaxableBase = taxableBase,
                Liability = liability,
                InvoiceCount = invoiceCount,
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
            TotalInvoiceCount = rows.Sum(r => r.InvoiceCount),
        };
    }

    private static decimal ToFunctional(decimal amount, decimal fxRate) =>
        fxRate == 1m ? amount : Math.Round(amount * fxRate, 2, MidpointRounding.AwayFromZero);

    private static string? NormalizeJurisdiction(InvoiceTaxRow row) =>
        string.IsNullOrWhiteSpace(row.State) ? null : row.State.Trim().ToUpperInvariant();

    private sealed record InvoiceTaxRow(
        string? State, decimal TaxRate, decimal FxRate, bool MarketplaceCollected, decimal TaxableSubtotal);
}
