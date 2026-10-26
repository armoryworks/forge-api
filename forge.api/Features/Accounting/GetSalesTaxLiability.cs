using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Sales-tax liability report — the seller sales-tax the business owes,
/// aggregated by jurisdiction (ship-to US state) over an optional date range,
/// in functional currency. No mutation, so no <c>ActivityLog</c>.
///
/// <para><b>Ledger basis (CAP-ACCT-FULLGL on).</b> Each invoice contributes the
/// Sales Tax Payable amount its AR entry actually posted, by journal entry date,
/// so the report total equals the account's invoice credits for the period. A
/// voided invoice is treated as the GL treats it: its original entry counts in
/// its own period and the void reversal counts, negatively, in the reversal's
/// period (the same period unless that one was hard-closed).</para>
///
/// <para><b>Invoice basis (CAP-ACCT-FULLGL off).</b> Finalized invoices (not
/// Draft, not Voided) by invoice date, each contributing the tax
/// <see cref="InvoiceArPostingService.ComputePostedAmounts"/> would post, times
/// the booking <see cref="Invoice.FxRate"/> rounded the way the engine rounds.</para>
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

public class GetSalesTaxLiabilityHandler(AppDbContext db, ICapabilitySnapshotProvider? capabilities = null)
    : IRequestHandler<GetSalesTaxLiabilityQuery, SalesTaxLiabilityReport>
{
    private const string FullGlCapability = "CAP-ACCT-FULLGL";
    private const string KeySalesTaxPayable = "SALES_TAX_PAYABLE";
    private const string RevenueKeySuffix = ":REVENUE";

    public async Task<SalesTaxLiabilityReport> Handle(
        GetSalesTaxLiabilityQuery request, CancellationToken cancellationToken)
    {
        var bookId = capabilities?.IsEnabled(FullGlCapability) == true
            ? await db.Books.AsNoTracking()
                .Where(b => b.IsActive)
                .OrderBy(b => b.Id)
                .Select(b => (int?)b.Id)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var contributions = bookId is int id
            ? await LedgerContributionsAsync(id, request, cancellationToken)
            : await InvoiceContributionsAsync(request, cancellationToken);

        var rows = new List<SalesTaxLiabilityJurisdictionRow>();

        foreach (var group in contributions.GroupBy(c => NormalizeJurisdiction(c.Invoice.State)))
        {
            decimal taxableBase = 0m;
            decimal liability = 0m;
            var invoiceCount = 0;

            foreach (var byInvoice in group.GroupBy(c => c.Invoice.Id))
            {
                var invoice = byInvoice.First().Invoice;
                var direction = byInvoice.Sum(c => c.Direction);
                var tax = byInvoice.Sum(c => c.Tax);
                if (direction == 0 && tax == 0m)
                    continue;

                invoiceCount++;

                if (invoice.MarketplaceCollected)
                    continue;

                taxableBase += direction * ToFunctional(TaxableSubtotal(invoice), invoice.FxRate);
                liability += tax;
            }

            if (invoiceCount == 0)
                continue;

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

    private async Task<List<Contribution>> LedgerContributionsAsync(
        int bookId, GetSalesTaxLiabilityQuery request, CancellationToken ct)
    {
        var taxAccounts = await db.AccountDeterminationRules.AsNoTracking()
            .Where(r => r.BookId == bookId && r.Key == KeySalesTaxPayable)
            .Select(r => r.GlAccountId)
            .Distinct()
            .ToListAsync(ct);

        var entries = db.JournalEntries.AsNoTracking()
            .Where(e => e.BookId == bookId
                && (e.Status == JournalEntryStatus.Posted || e.Status == JournalEntryStatus.Reversed));
        if (request.FromDate is { } from)
            entries = entries.Where(e => e.EntryDate >= from);
        if (request.ToDate is { } to)
            entries = entries.Where(e => e.EntryDate <= to);

        var originals = await entries
            .Where(e => e.ReversalOfEntryId == null
                && e.Source == JournalSource.AR
                && e.SourceType == "Invoice"
                && e.SourceId != null
                && e.IdempotencyKey != null && e.IdempotencyKey.EndsWith(RevenueKeySuffix))
            .Select(e => new LedgerRow(
                e.SourceId!.Value,
                1,
                e.Lines.Where(l => taxAccounts.Contains(l.GlAccountId))
                    .Sum(l => l.Credit > 0m ? l.FunctionalAmount : -l.FunctionalAmount)))
            .ToListAsync(ct);

        var reversals = await entries
            .Where(e => e.ReversalOfEntry != null
                && e.ReversalOfEntry.Source == JournalSource.AR
                && e.ReversalOfEntry.SourceType == "Invoice"
                && e.ReversalOfEntry.SourceId != null
                && e.ReversalOfEntry.IdempotencyKey != null
                && e.ReversalOfEntry.IdempotencyKey.EndsWith(RevenueKeySuffix))
            .Select(e => new LedgerRow(
                e.ReversalOfEntry!.SourceId!.Value,
                -1,
                e.Lines.Where(l => taxAccounts.Contains(l.GlAccountId))
                    .Sum(l => l.Credit > 0m ? l.FunctionalAmount : -l.FunctionalAmount)))
            .ToListAsync(ct);

        var ledger = originals.Concat(reversals).ToList();
        var invoiceIds = ledger.Select(r => (int)r.InvoiceId).Distinct().ToList();
        var invoices = (await LoadInvoicesAsync(
                db.Invoices.IgnoreQueryFilters().Where(i => invoiceIds.Contains(i.Id)), ct))
            .ToDictionary(i => i.Id);

        return ledger
            .Where(r => invoices.ContainsKey((int)r.InvoiceId))
            .Select(r => new Contribution(invoices[(int)r.InvoiceId], r.Direction, r.Tax))
            .ToList();
    }

    private async Task<List<Contribution>> InvoiceContributionsAsync(
        GetSalesTaxLiabilityQuery request, CancellationToken ct)
    {
        DateTimeOffset? fromBound = request.FromDate is { } f
            ? new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;
        DateTimeOffset? toBound = request.ToDate is { } t
            ? new DateTimeOffset(t.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero)
            : null;

        var query = db.Invoices
            .Where(i => i.Status != InvoiceStatus.Draft && i.Status != InvoiceStatus.Voided);
        if (fromBound is not null)
            query = query.Where(i => i.InvoiceDate >= fromBound);
        if (toBound is not null)
            query = query.Where(i => i.InvoiceDate <= toBound);

        return (await LoadInvoicesAsync(query, ct))
            .Select(i => new Contribution(i, 1, ToFunctional(PostedTax(i), i.FxRate)))
            .ToList();
    }

    private static async Task<List<InvoiceTaxRow>> LoadInvoicesAsync(IQueryable<Invoice> query, CancellationToken ct)
        => await query.AsNoTracking()
            .Select(i => new InvoiceTaxRow(
                i.Id,
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
                i.Lines.Select(l => new InvoiceLine
                {
                    LineNumber = l.LineNumber,
                    Description = l.Description,
                    Quantity = l.Quantity,
                    UnitPrice = l.UnitPrice,
                    TaxCode = l.TaxCode,
                }).ToList()))
            .ToListAsync(ct);

    private static decimal TaxableSubtotal(InvoiceTaxRow invoice) =>
        invoice.Lines.Where(l => l.IsTaxable).Sum(l => l.LineTotal);

    private static decimal PostedTax(InvoiceTaxRow invoice) =>
        InvoiceArPostingService.ComputePostedAmounts(
            new Invoice { TaxRate = invoice.TaxRate, Lines = invoice.Lines.ToList() }).TaxAmount;

    private static decimal ToFunctional(decimal amount, decimal fxRate) =>
        fxRate == 1m ? amount : Math.Round(amount * fxRate, 2, MidpointRounding.AwayFromZero);

    private static string? NormalizeJurisdiction(string? state) =>
        string.IsNullOrWhiteSpace(state) ? null : state.Trim().ToUpperInvariant();

    private sealed record InvoiceTaxRow(
        int Id, string? State, decimal TaxRate, decimal FxRate, bool MarketplaceCollected, List<InvoiceLine> Lines);

    private sealed record LedgerRow(long InvoiceId, int Direction, decimal Tax);

    private sealed record Contribution(InvoiceTaxRow Invoice, int Direction, decimal Tax);
}
