using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.CreditMemos;
using Forge.Api.Features.CustomerReturns;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting.CreditMemos;

/// <summary>
/// A FULLGL book with the accounts credit memos, refunds and returns post to, plus builders for the handlers under
/// test, all on one InMemory context.
/// </summary>
internal sealed class CreditMemoTestContext : IDisposable
{
    public const int BookId = 1;
    public const int UsdId = 1;
    public const int EurId = 2;
    public const int CashId = 100;
    public const int ArId = 101;
    public const int RevenueId = 102;
    public const int TaxId = 103;
    public const int ReturnsId = 104;
    public const int FreightIncomeId = 105;
    public const int BadDebtId = 106;
    public const int DepositsId = 107;
    public const int FxGainId = 108;
    public const int FxLossId = 109;
    public const int CogsId = 110;
    public const int InventoryFgId = 111;
    public const int WritedownId = 112;
    public const int InventoryRawId = 113;

    public static readonly DateTimeOffset Today = new(2026, 3, 10, 15, 0, 0, TimeSpan.Zero);

    public AppDbContext Db { get; }
    public FixedClock Clock { get; } = new(Today);
    public bool FullGl { get; set; } = true;

    private CreditMemoTestContext(AppDbContext db) => Db = db;

    public sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default) => Task.FromResult(_next++);
    }

    private sealed class Capabilities(CreditMemoTestContext owner) : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current => new(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["CAP-ACCT-FULLGL"] = owner.FullGl },
            DateTimeOffset.UtcNow);

        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    public static async Task<CreditMemoTestContext> CreateAsync()
    {
        var db = TestDbContextFactory.Create();
        db.CurrentUserId = 7;
        db.Set<Currency>().AddRange(
            new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$", IsBaseCurrency = true, IsActive = true },
            new Currency { Id = EurId, Code = "EUR", Name = "Euro", Symbol = "€", IsActive = true });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/Denver", RoundingTolerance = 0.01m, IsActive = true,
        });
        db.Set<FiscalYear>().Add(new FiscalYear
        {
            Id = 10, BookId = BookId, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open,
        });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = 1000, FiscalYearId = 10, PeriodNumber = 1, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open,
        });

        var accounts = new (int Id, string Number, string Name, AccountType Type, NormalBalance Normal, ControlAccountType? Control, string Key)[]
        {
            (CashId, "10100", "Cash", AccountType.Asset, NormalBalance.Debit, null, "CASH"),
            (ArId, "11000", "Accounts Receivable", AccountType.Asset, NormalBalance.Debit, ControlAccountType.AR, "AR_CONTROL"),
            (InventoryRawId, "13100", "Inventory Raw", AccountType.Asset, NormalBalance.Debit, ControlAccountType.Inventory, "INVENTORY_RAW"),
            (InventoryFgId, "13300", "Inventory FG", AccountType.Asset, NormalBalance.Debit, ControlAccountType.Inventory, "INVENTORY_FG"),
            (TaxId, "23000", "Sales Tax Payable", AccountType.Liability, NormalBalance.Credit, null, "SALES_TAX_PAYABLE"),
            (DepositsId, "24500", "Customer Deposits", AccountType.Liability, NormalBalance.Credit, null, "CUSTOMER_DEPOSITS"),
            (RevenueId, "40000", "Sales Revenue", AccountType.Income, NormalBalance.Credit, null, "SALES_REVENUE"),
            (FreightIncomeId, "40500", "Freight Income", AccountType.Income, NormalBalance.Credit, null, "FREIGHT_INCOME"),
            (ReturnsId, "41000", "Sales Returns & Allowances", AccountType.Income, NormalBalance.Debit, null, "SALES_RETURNS"),
            (CogsId, "50000", "Cost of Goods Sold", AccountType.Expense, NormalBalance.Debit, null, "COGS"),
            (WritedownId, "52000", "Inventory Write-Down", AccountType.Expense, NormalBalance.Debit, null, "INVENTORY_WRITEDOWN"),
            (BadDebtId, "69500", "Bad Debt Expense", AccountType.Expense, NormalBalance.Debit, null, "BAD_DEBT_EXPENSE"),
            (FxGainId, "90000", "FX Gain", AccountType.Income, NormalBalance.Credit, null, "FX_GAIN"),
            (FxLossId, "90100", "FX Loss", AccountType.Expense, NormalBalance.Debit, null, "FX_LOSS"),
        };
        foreach (var a in accounts)
        {
            db.Set<GlAccount>().Add(new GlAccount
            {
                Id = a.Id, BookId = BookId, AccountNumber = a.Number, Name = a.Name, AccountType = a.Type, NormalBalance = a.Normal,
                IsControlAccount = a.Control is not null, ControlType = a.Control, IsPostable = true, IsActive = true,
            });
            db.Set<AccountDeterminationRule>().Add(new AccountDeterminationRule { BookId = BookId, Key = a.Key, GlAccountId = a.Id });
        }

        await db.SaveChangesAsync();
        return new CreditMemoTestContext(db);
    }

    public IPostingEngine Engine() => new ForgeGlPostingEngine(Db, new AccountDeterminationResolver(Db), new FakeAllocator(), Clock);

    public ICapabilitySnapshotProvider Caps() => new Capabilities(this);

    public CreditMemoPostingService Posting() => new(Db, Engine(), Caps());

    public InvoiceArPostingService InvoicePosting() => new(Db, Engine(), Caps(), valuation: new InventoryValuationService(Db));

    public CreditMemoIssuer Issuer() => new(Db, Posting(), Clock);

    public ReturnDispositionService Dispositions() => new(Db, Engine(), Caps(), Clock, new InventoryValuationService(Db));

    public ResolveCustomerReturnHandler ResolveHandler() => new(Db, Dispositions(), Issuer(), Clock);

    public async Task<Customer> CustomerAsync(string name = "Acme Corp")
    {
        var customer = new Customer { Name = name };
        Db.Customers.Add(customer);
        await Db.SaveChangesAsync();
        return customer;
    }

    /// <summary>A sent invoice posted through the real AR posting service (revenue, tax, COGS and the open item).</summary>
    public async Task<Invoice> PostedInvoiceAsync(
        Customer customer, IEnumerable<InvoiceLine> lines, decimal taxRate = 0m, string number = "INV-1001",
        int currencyId = UsdId, decimal fxRate = 1m)
    {
        var invoice = new Invoice
        {
            InvoiceNumber = number, CustomerId = customer.Id, CurrencyId = currencyId, FxRate = fxRate,
            Status = InvoiceStatus.Sent, InvoiceDate = Today.AddDays(-20), DueDate = Today.AddDays(10), TaxRate = taxRate,
            Lines = lines.ToList(),
        };
        Db.Invoices.Add(invoice);
        await Db.SaveChangesAsync();
        await InvoicePosting().PostInvoiceFinalizedAsync(invoice.Id, 7);
        return invoice;
    }

    public async Task<Part> FinishedGoodAsync(decimal standardCost = 100m, string number = "WIDGET-1")
    {
        var part = new Part
        {
            PartNumber = number, Name = number, InventoryClass = InventoryClass.FinishedGood,
            ProcurementSource = ProcurementSource.Make, ManualCostOverride = standardCost,
        };
        Db.Parts.Add(part);
        await Db.SaveChangesAsync();
        return part;
    }

    public async Task<StorageLocation> LocationAsync()
    {
        var location = new StorageLocation { Name = "Returns shelf", LocationType = LocationType.Bin };
        Db.StorageLocations.Add(location);
        await Db.SaveChangesAsync();
        return location;
    }

    public async Task<Dictionary<int, decimal>> NetByAccountAsync()
        => (await Db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).ToListAsync())
            .SelectMany(e => e.Lines)
            .GroupBy(l => l.GlAccountId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Debit > 0m ? l.FunctionalAmount : -l.FunctionalAmount));

    public async Task<decimal> SignedOpenItemsAsync()
        => (await Db.ArOpenItems.IgnoreQueryFilters().Where(i => i.Status != OpenItemStatus.Voided).ToListAsync())
            .Sum(i => i.OpenFunctionalAmount);

    public void Dispose() => Db.Dispose();
}
