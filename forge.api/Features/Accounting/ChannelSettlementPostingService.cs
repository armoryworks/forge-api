using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Serilog;

namespace Forge.Api.Features.Accounting;

/// <inheritdoc />
public sealed class ChannelSettlementPostingService(
    AppDbContext db,
    IPostingEngine postingEngine,
    ICapabilitySnapshotProvider capabilities) : IChannelSettlementPostingService
{
    private const string FullGlCapability = "CAP-ACCT-FULLGL";

    internal const string KeyCash = "CASH";
    internal const string KeyArControl = "AR_CONTROL";
    internal const string KeyCustomerDeposits = "CUSTOMER_DEPOSITS";
    internal const string KeySalesTaxPayable = "SALES_TAX_PAYABLE";
    internal const string KeySalesReturns = "SALES_RETURNS";
    internal const string KeyMarketplaceFees = "MARKETPLACE_FEES";
    internal const string KeyMarketplaceReserve = "MARKETPLACE_RESERVE";

    internal static string IdempotencyKey(int settlementId) =>
        $"{JournalSource.AR}:ChannelSettlement:{settlementId}:SETTLEMENT";

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

        return settlementIds.Where(id => posted.Contains(IdempotencyKey(id))).ToHashSet();
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

        var arCredits = await RelieveOpenItemsAsync(book.Id, proceedsByOrder, ct);
        var relieved = arCredits.Values.Sum();
        Add(KeyCustomerDeposits, -(proceedsByOrder.Values.Sum() + unappliedProceeds - relieved));

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

        foreach (var (customerId, amount) in arCredits.OrderBy(kv => kv.Key))
        {
            if (amount == 0m)
                continue;
            lines.Add(new PostingLine
            {
                AccountKey = KeyArControl,
                PartyType = SubledgerPartyType.Customer,
                PartyId = customerId,
                Credit = amount > 0m ? amount : 0m,
                Debit = amount < 0m ? -amount : 0m,
                Description = $"AR settled by marketplace payout — {label}",
            });
        }

        if (lines.Count == 0)
            return;

        var entryDate = DateOnly.FromDateTime((settlement.DepositedAt ?? settlement.PeriodEnd).UtcDateTime);
        await postingEngine.PostAsSystemAsync(new PostingRequest
        {
            BookId = book.Id,
            EntryDate = entryDate,
            Source = JournalSource.AR,
            SourceType = "ChannelSettlement",
            SourceId = settlement.Id,
            CurrencyId = book.FunctionalCurrencyId,
            Memo = $"Marketplace payout — {label}",
            IdempotencyKey = key,
            Lines = lines,
        }, actingUserId, ct);
    }

    private async Task<Dictionary<int, decimal>> RelieveOpenItemsAsync(
        int bookId, Dictionary<int, decimal> proceedsByOrder, CancellationToken ct)
    {
        var credits = new Dictionary<int, decimal>();
        if (proceedsByOrder.Count == 0)
            return credits;

        var orderIds = proceedsByOrder.Keys.ToList();
        var invoices = await db.Invoices.AsNoTracking()
            .Where(i => i.SalesOrderId != null && orderIds.Contains(i.SalesOrderId.Value)
                && i.Status != InvoiceStatus.Draft && i.Status != InvoiceStatus.Voided)
            .Select(i => new { i.Id, SalesOrderId = i.SalesOrderId!.Value, i.InvoiceDate })
            .ToListAsync(ct);
        var invoiceIds = invoices.Select(i => i.Id).ToList();
        var openItems = await db.ArOpenItems.IgnoreQueryFilters()
            .Where(o => o.BookId == bookId && o.SourceType == "Invoice" && invoiceIds.Contains(o.SourceId)
                && o.Status != OpenItemStatus.Voided)
            .ToListAsync(ct);

        foreach (var (orderId, proceeds) in proceedsByOrder)
        {
            var remaining = proceeds;
            var items = invoices
                .Where(i => i.SalesOrderId == orderId)
                .OrderBy(i => i.InvoiceDate).ThenBy(i => i.Id)
                .Select(i => openItems.FirstOrDefault(o => o.SourceId == i.Id))
                .Where(o => o is not null && o.FxRate == 1m)
                .Select(o => o!);
            foreach (var item in items)
            {
                if (remaining <= 0m)
                    break;
                var apply = Math.Min(remaining, item.OpenTxnAmount);
                if (apply <= 0m)
                    continue;
                item.AppliedTxnAmount += apply;
                item.AppliedFunctionalAmount += apply;
                item.RecomputeStatus();
                credits[item.CustomerId] = credits.GetValueOrDefault(item.CustomerId) + apply;
                remaining -= apply;
            }
        }

        return credits;
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
