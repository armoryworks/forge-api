using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.Reports;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Reports;

public class GetSimplePnlReportLedgerTests : IDisposable
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int CadId = 2;
    private const string FullGl = "CAP-ACCT-FULLGL";

    private static readonly DateTimeOffset October = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PeriodStart = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PeriodEnd = new(2026, 10, 31, 23, 59, 59, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private sealed class Allocator(AppDbContext db) : IAcctNumberSequenceAllocator
    {
        public async Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => (await db.JournalEntries.IgnoreQueryFilters()
                .Where(e => e.BookId == bookId && e.FiscalYearId == fiscalYearId)
                .MaxAsync(e => (long?)e.EntryNumber, ct) ?? 0) + 1;
    }

    private ForgeGlPostingEngine Engine() =>
        new(_db, new AccountDeterminationResolver(_db), new Allocator(_db), new SystemClock());

    private GetSimplePnlReportHandler Handler(bool fullGlOn) => new(
        new ReportRepository(_db),
        _db,
        new FinancialStatementService(_db, new SystemClock()),
        fullGlOn ? new StubCapabilitySnapshotProvider(FullGl) : StubCapabilitySnapshotProvider.Off);

    private async Task SeedAsync()
    {
        _db.Set<Currency>().AddRange(
            new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" },
            new Currency { Id = CadId, Code = "CAD", Name = "Canadian Dollar", Symbol = "$" });
        _db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/Denver", RoundingTolerance = 0.01m, IsActive = true,
            RevenueRecognitionMethod = RevenueRecognitionMethod.PointInTime,
        });
        _db.Set<FiscalYear>().Add(new FiscalYear
        {
            Id = 10, BookId = BookId, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open,
        });
        _db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = 1000, FiscalYearId = 10, PeriodNumber = 10, Name = "Oct 2026",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31), Status = FiscalPeriodStatus.Open,
        });
        _db.Set<GlAccount>().AddRange(
            new GlAccount { Id = 110, BookId = BookId, AccountNumber = "11000", Name = "Accounts Receivable", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.AR, IsPostable = true, IsActive = true },
            new GlAccount { Id = 133, BookId = BookId, AccountNumber = "13300", Name = "Inventory — Finished Goods", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = 230, BookId = BookId, AccountNumber = "23000", Name = "Sales Tax Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = 240, BookId = BookId, AccountNumber = "24000", Name = "Deferred Revenue", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = 400, BookId = BookId, AccountNumber = "40000", Name = "Sales Revenue", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = 500, BookId = BookId, AccountNumber = "50000", Name = "Cost of Goods Sold", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        _db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "AR_CONTROL", GlAccountId = 110 },
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_FG", GlAccountId = 133 },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_TAX_PAYABLE", GlAccountId = 230 },
            new AccountDeterminationRule { BookId = BookId, Key = "DEFERRED_REVENUE", GlAccountId = 240 },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_REVENUE", GlAccountId = 400 },
            new AccountDeterminationRule { BookId = BookId, Key = "COGS", GlAccountId = 500 });
        _db.Customers.Add(new Customer { Id = 1, Name = "Export buyer" });
        await _db.SaveChangesAsync();
    }

    private async Task<Invoice> AddCadInvoiceAsync()
    {
        var invoice = new Invoice
        {
            InvoiceNumber = "INV-CAD-1",
            CustomerId = 1,
            CurrencyId = CadId,
            FxRate = 0.73m,
            Status = InvoiceStatus.Sent,
            InvoiceDate = October,
            DueDate = October.AddDays(30),
        };
        invoice.Lines.Add(new InvoiceLine { Description = "Widget", Quantity = 10m, UnitPrice = 100m, LineNumber = 1 });
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();
        return invoice;
    }

    [Fact]
    public async Task Ledger_basis_reports_gl_revenue_and_cogs_in_functional_currency()
    {
        await SeedAsync();
        var invoice = await AddCadInvoiceAsync();
        await new InvoiceArPostingService(_db, Engine(), new StubCapabilitySnapshotProvider(FullGl))
            .PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId: 1);
        await Engine().PostAsSystemAsync(new PostingRequest
        {
            BookId = BookId,
            EntryDate = new DateOnly(2026, 10, 12),
            Source = JournalSource.Inventory,
            SourceType = "Test",
            SourceId = 1,
            CurrencyId = UsdId,
            IdempotencyKey = "Inventory:Test:1:COGS",
            Lines =
            [
                new PostingLine { AccountKey = "COGS", Debit = 400m },
                new PostingLine { AccountKey = "INVENTORY_FG", Credit = 400m },
            ],
        }, 1);

        var rows = await Handler(fullGlOn: true).Handle(new GetSimplePnlReportQuery(PeriodStart, PeriodEnd), default);

        rows.Should().OnlyContain(r => r.Basis == SimplePnlReportItem.LedgerBasis);
        rows.Where(r => r.Type == "Revenue").Sum(r => r.Amount).Should().Be(730m);
        rows.Should().ContainSingle(r => r.Type == "Expense").Which.Should().Be(
            new SimplePnlReportItem("50000 Cost of Goods Sold", "Expense", 400m, SimplePnlReportItem.LedgerBasis));
    }

    [Fact]
    public async Task Invoice_basis_converts_to_functional_currency_and_says_so()
    {
        await SeedAsync();
        await AddCadInvoiceAsync();

        var rows = await Handler(fullGlOn: false).Handle(new GetSimplePnlReportQuery(PeriodStart, PeriodEnd), default);

        var revenue = rows.Should().ContainSingle(r => r.Type == "Revenue").Subject;
        revenue.Amount.Should().Be(730m);
        revenue.Basis.Should().Be(SimplePnlReportItem.InvoiceBasis);
        revenue.Category.Should().Contain("invoice basis");
    }

    public void Dispose() => _db.Dispose();
}
