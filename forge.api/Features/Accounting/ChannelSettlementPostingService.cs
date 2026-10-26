using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Payments;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Extensions;
using Serilog;

namespace Forge.Api.Features.Accounting;

/// <inheritdoc />
public sealed class ChannelSettlementPostingService(
    AppDbContext db,
    IPostingEngine postingEngine,
    ICapabilitySnapshotProvider capabilities,
    IPaymentCashPostingService cashPosting,
    IPaymentRepository paymentNumbers,
    IBusinessIdentifierService? identifiers = null) : IChannelSettlementPostingService
{
    private const string FullGlCapability = "CAP-ACCT-FULLGL";

    internal const string KeyCash = "CASH";
    internal const string KeyCustomerDeposits = "CUSTOMER_DEPOSITS";
    internal const string KeySalesTaxPayable = "SALES_TAX_PAYABLE";
    internal const string KeySalesReturns = "SALES_RETURNS";
    internal const string KeyMarketplaceFees = "MARKETPLACE_FEES";
    internal const string KeyMarketplaceReserve = "MARKETPLACE_RESERVE";

    internal static string IdempotencyKey(int settlementId) =>
        $"{JournalSource.AR}:ChannelSettlement:{settlementId}:SETTLEMENT";

    internal static string PayoutPaymentNotes(int settlementId, string externalSettlementId) =>
        $"Marketplace payout — channel settlement {settlementId} ({externalSettlementId})";

    public async Task<IReadOnlySet<int>> PostedSettlementIdsAsync(
        IReadOnlyCollection<int> settlementIds, CancellationToken ct = default)
    {
        if (settlementIds.Count == 0 || !capabilities.IsEnabled(FullGlCapability))
            return new HashSet<int>();

        var keys = settlementIds.Select(IdempotencyKey).ToList();
        var posted = await db.JournalEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.IdempotencyKey != null && keys.Contains(e.IdempotencyKey))
            .Select(e => e.IdempotencyKey!)
            .ToListAsync(ct);

        var notes = await db.ChannelSettlements.AsNoTracking()
            .Where(s => settlementIds.Contains(s.Id))
            .Select(s => new { s.Id, s.ExternalSettlementId })
            .ToListAsync(ct);
        var markers = notes.Select(n => PayoutPaymentNotes(n.Id, n.ExternalSettlementId)).ToList();
        var paid = await db.Payments.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Notes != null && markers.Contains(p.Notes))
            .Select(p => p.Notes!)
            .ToListAsync(ct);

        return notes
            .Where(n => posted.Contains(IdempotencyKey(n.Id)) || paid.Contains(PayoutPaymentNotes(n.Id, n.ExternalSettlementId)))
            .Select(n => n.Id)
            .ToHashSet();
    }

    public async Task PostSettlementAsync(int settlementId, int actingUserId, CancellationToken ct = default)
    {
        if (!capabilities.IsEnabled(FullGlCapability))
            return;

        var book = await db.Books.AsNoTracking()
            .Include(b => b.FunctionalCurrency)
            .Where(b => b.IsActive)
            .OrderBy(b => b.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new PostingException(
                "NO_POSTING_BOOK",
                "CAP-ACCT-FULLGL is enabled but no active accounting Book is seeded to post the settlement into.");

        var key = IdempotencyKey(settlementId);
        if (await db.JournalEntries.IgnoreQueryFilters().AnyAsync(e => e.BookId == book.Id && e.IdempotencyKey == key, ct))
            return;

        var settlement = await db.ChannelSettlements
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == settlementId, ct);
        if (settlement is null)
        {
            Log.Warning("Settlement posting skipped: settlement {SettlementId} not found (FULLGL on).", settlementId);
            return;
        }

        var payoutNotes = PayoutPaymentNotes(settlement.Id, settlement.ExternalSettlementId);
        if (await db.Payments.IgnoreQueryFilters().AnyAsync(p => p.Notes == payoutNotes, ct))
            return;

        if (!string.Equals(settlement.CurrencyCode, book.FunctionalCurrency.Code, StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning(
                "Settlement posting skipped: settlement {External} is in {Currency}, the book's functional currency is {Functional}.",
                settlement.ExternalSettlementId, settlement.CurrencyCode, book.FunctionalCurrency.Code);
            return;
        }

        var net = new Dictionary<string, decimal>(StringComparer.Ordinal);
        void Add(string accountKey, decimal debitPositive) =>
            net[accountKey] = net.GetValueOrDefault(accountKey) + debitPositive;

        Add(KeyCash, settlement.ReportedNetAmount);

        var proceedsByOrder = new Dictionary<int, decimal>();
        decimal unappliedProceeds = 0m;
        foreach (var line in settlement.Lines)
        {
            switch (line.LineType)
            {
                case ChannelSettlementLineType.OrderProceeds:
                case ChannelSettlementLineType.ShippingIncome:
                    if (line.SalesOrderId is int orderId)
                        proceedsByOrder[orderId] = proceedsByOrder.GetValueOrDefault(orderId) + line.Amount;
                    else
                        unappliedProceeds += line.Amount;
                    break;
                case ChannelSettlementLineType.TaxCollected:
                    Add(KeySalesTaxPayable, -line.Amount);
                    break;
                case ChannelSettlementLineType.Refund:
                    Add(KeySalesReturns, -line.Amount);
                    break;
                case ChannelSettlementLineType.ReserveAdjustment:
                    Add(KeyMarketplaceReserve, -line.Amount);
                    break;
                default:
                    Add(KeyMarketplaceFees, -line.Amount);
                    break;
            }
        }

        Add(KeyMarketplaceFees, -settlement.Variance);

        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;

        var entryDateTime = settlement.DepositedAt ?? settlement.PeriodEnd;
        var applied = await PayInvoicesAsync(book, settlement, payoutNotes, entryDateTime, proceedsByOrder, actingUserId, ct);
        Add(KeyCash, -applied);
        Add(KeyCustomerDeposits, -(proceedsByOrder.Values.Sum() + unappliedProceeds - applied));

        var label = $"settlement {settlement.ExternalSettlementId}";
        var lines = new List<PostingLine>();
        foreach (var (accountKey, amount) in net.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (amount == 0m)
                continue;
            lines.Add(new PostingLine
            {
                AccountKey = accountKey,
                Debit = amount > 0m ? amount : 0m,
                Credit = amount < 0m ? -amount : 0m,
                Description = $"{Describe(accountKey)} — {label}",
            });
        }

        if (lines.Count > 0)
        {
            await postingEngine.PostAsSystemAsync(new PostingRequest
            {
                BookId = book.Id,
                EntryDate = DateOnly.FromDateTime(entryDateTime.UtcDateTime),
                Source = JournalSource.AR,
                SourceType = "ChannelSettlement",
                SourceId = settlement.Id,
                CurrencyId = book.FunctionalCurrencyId,
                Memo = $"Marketplace payout — {label}",
                IdempotencyKey = key,
                Lines = lines,
            }, actingUserId, ct);
        }

        if (tx is not null)
            await tx.CommitAsync(ct);
    }

    private async Task<decimal> PayInvoicesAsync(
        Book book,
        ChannelSettlement settlement,
        string payoutNotes,
        DateTimeOffset paymentDate,
        Dictionary<int, decimal> proceedsByOrder,
        int actingUserId,
        CancellationToken ct)
    {
        if (proceedsByOrder.Count == 0)
            return 0m;

        var orderIds = proceedsByOrder.Keys.ToList();
        var invoices = await db.Invoices
            .Include(i => i.Lines)
            .Include(i => i.PaymentApplications)
            .Include(i => i.CreditApplications)
            .Where(i => i.SalesOrderId != null && orderIds.Contains(i.SalesOrderId.Value)
                && (i.Status == InvoiceStatus.Sent || i.Status == InvoiceStatus.PartiallyPaid || i.Status == InvoiceStatus.Overdue)
                && i.CurrencyId == book.FunctionalCurrencyId && i.FxRate == 1m)
            .OrderBy(i => i.InvoiceDate).ThenBy(i => i.Id)
            .ToListAsync(ct);

        var payments = new Dictionary<int, Payment>();
        var completionCandidates = new HashSet<int>();
        foreach (var (orderId, proceeds) in proceedsByOrder)
        {
            var remaining = proceeds;
            foreach (var invoice in invoices.Where(i => i.SalesOrderId == orderId))
            {
                if (remaining <= 0m)
                    break;
                var apply = Math.Min(remaining, invoice.BalanceDue);
                if (apply <= 0m)
                    continue;

                if (!payments.TryGetValue(invoice.CustomerId, out var payment))
                {
                    payment = new Payment
                    {
                        CustomerId = invoice.CustomerId,
                        CurrencyId = book.FunctionalCurrencyId,
                        FxRate = 1m,
                        Method = PaymentMethod.BankTransfer,
                        PaymentDate = paymentDate,
                        ReferenceNumber = settlement.ExternalSettlementId.Length > 50
                            ? settlement.ExternalSettlementId[..50]
                            : settlement.ExternalSettlementId,
                        Notes = payoutNotes,
                    };
                    payments[invoice.CustomerId] = payment;
                }

                await PaymentApplicationRules.ApplyToInvoiceAsync(
                    db, payment, invoice,
                    new CreatePaymentApplicationModel(invoice.Id, apply, DiscountAmount: 0m),
                    completionCandidates, Forge.Data.Services.DiscountSettings.Default, null,
                    "DiscountAmount", ct);
                payment.Amount += apply;
                remaining -= apply;
            }
        }

        foreach (var payment in payments.Values)
        {
            payment.PaymentNumber = await paymentNumbers.GenerateNextPaymentNumberAsync(ct);
            db.Payments.Add(payment);
            await db.SaveChangesAsync(ct);
            if (identifiers is not null)
                await identifiers.IssueAsync(BusinessEntityType.Payment, payment.Id, payment.PaymentNumber, ct);
            db.LogActivityAt("created",
                $"Payment {payment.PaymentNumber} recorded from marketplace settlement {settlement.ExternalSettlementId}",
                ("Payment", payment.Id), ("Customer", payment.CustomerId));
            await db.SaveChangesAsync(ct);
            await cashPosting.PostPaymentCreatedAsync(payment.Id, actingUserId, ct);
        }

        await PaymentApplicationRules.CompletePaidSalesOrdersAsync(db, completionCandidates, ct);

        return payments.Values.Sum(p => p.Amount);
    }

    private static string Describe(string accountKey) => accountKey switch
    {
        KeyCash => "Marketplace payout",
        KeyMarketplaceFees => "Marketplace fees",
        KeySalesTaxPayable => "Sales tax remitted by the marketplace",
        KeySalesReturns => "Marketplace refunds",
        KeyMarketplaceReserve => "Marketplace reserve",
        KeyCustomerDeposits => "Marketplace proceeds with no open invoice",
        _ => accountKey,
    };
}
