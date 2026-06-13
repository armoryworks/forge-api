using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <inheritdoc />
public sealed class CashFlowStatementService(AppDbContext db, IClock clock) : ICashFlowStatementService
{
    private const string KeyCash = "CASH";

    public async Task<CashFlowStatement> GetCashFlowStatementAsync(
        int bookId, DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken ct = default)
    {
        var asOf = toDate ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        var tolerance = await db.Books.AsNoTracking()
            .Where(b => b.Id == bookId).Select(b => (decimal?)b.RoundingTolerance).FirstOrDefaultAsync(ct) ?? 0.01m;

        var cashAccountIds = (await db.AccountDeterminationRules
            .Where(r => r.BookId == bookId && r.Key == KeyCash)
            .Select(r => r.GlAccountId)
            .ToListAsync(ct)).ToHashSet();

        // Windowed per-line activity. Exclude the year-end RE roll so net income isn't double-counted: it is
        // excluded from BOTH the P&L lead (below) and the equity/financing Δ, and reconciles either way.
        var lines = await
            (from line in db.JournalLines.IgnoreQueryFilters()
             join entry in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals entry.Id
             join account in db.GlAccounts.IgnoreQueryFilters() on line.GlAccountId equals account.Id
             where entry.BookId == bookId
                 && (entry.Status == JournalEntryStatus.Posted || entry.Status == JournalEntryStatus.Reversed)
                 && entry.SourceType != "YearEndClose"
                 && (fromDate == null || entry.EntryDate >= fromDate)
                 && entry.EntryDate <= asOf
             select new Row
             {
                 AccountId = account.Id,
                 AccountNumber = account.AccountNumber,
                 AccountName = account.Name,
                 AccountType = account.AccountType,
                 Category = account.CashFlowCategory,
                 NetDebit = line.Debit > 0 ? line.FunctionalAmount : -line.FunctionalAmount,
             })
            .ToListAsync(ct);

        // Net income = −Σ(net debit of P&L accounts).
        var netIncome = -lines
            .Where(r => r.AccountType is AccountType.Income or AccountType.Expense)
            .Sum(r => r.NetDebit);

        // Non-cash balance-sheet accounts, cash-flow signed (a use of cash = increase in a non-cash asset =
        // +Δ net debit → −Δ; a source = increase in a liability/equity = −Δ net debit → +).
        var bsAccounts = lines
            .Where(r => r.AccountType is AccountType.Asset or AccountType.Liability or AccountType.Equity
                && !cashAccountIds.Contains(r.AccountId))
            .GroupBy(r => new { r.AccountId, r.AccountNumber, r.AccountName, r.AccountType, r.Category })
            .Select(g => new
            {
                g.Key.AccountId, g.Key.AccountNumber, g.Key.AccountName,
                Section = SectionFor(g.Key.AccountType, g.Key.Category),
                CashFlow = -g.Sum(r => r.NetDebit),
            })
            .Where(x => x.CashFlow != 0m)
            .OrderBy(x => x.AccountNumber, StringComparer.Ordinal)
            .ToList();

        static CashFlowLine Line(int id, string num, string name, decimal amount)
            => new() { GlAccountId = id, AccountNumber = num, AccountName = name, Amount = amount };

        List<CashFlowLine> Section(CashFlowCategory section) => bsAccounts
            .Where(x => x.Section == section)
            .Select(x => Line(x.AccountId, x.AccountNumber, x.AccountName, x.CashFlow)).ToList();

        var operating = Section(CashFlowCategory.Operating);
        var investing = Section(CashFlowCategory.Investing);
        var financing = Section(CashFlowCategory.Financing);

        var netCashOperating = netIncome + operating.Sum(l => l.Amount);
        var netCashInvesting = investing.Sum(l => l.Amount);
        var netCashFinancing = financing.Sum(l => l.Amount);

        // Actual cash movement (cash accounts are never touched by the close, so this is exact).
        var actualCashChange = lines.Where(r => cashAccountIds.Contains(r.AccountId)).Sum(r => r.NetDebit);

        return new CashFlowStatement
        {
            BookId = bookId,
            FromDate = fromDate,
            ToDate = asOf,
            NetIncome = netIncome,
            OperatingAdjustments = operating,
            NetCashFromOperating = netCashOperating,
            Investing = investing,
            NetCashFromInvesting = netCashInvesting,
            Financing = financing,
            NetCashFromFinancing = netCashFinancing,
            NetChangeInCash = netCashOperating + netCashInvesting + netCashFinancing,
            ActualCashChange = actualCashChange,
            RoundingTolerance = tolerance,
        };
    }

    /// <summary>
    /// A non-cash balance-sheet account's cash-flow section: the explicit <c>GlAccount.CashFlowCategory</c>
    /// when tagged, else the type heuristic (Asset/Liability → Operating working capital, Equity → Financing).
    /// </summary>
    private static CashFlowCategory SectionFor(AccountType type, CashFlowCategory? tagged)
        => tagged ?? (type == AccountType.Equity ? CashFlowCategory.Financing : CashFlowCategory.Operating);

    private sealed class Row
    {
        public int AccountId { get; init; }
        public string AccountNumber { get; init; } = string.Empty;
        public string AccountName { get; init; } = string.Empty;
        public AccountType AccountType { get; init; }
        public CashFlowCategory? Category { get; init; }
        public decimal NetDebit { get; init; }
    }
}
