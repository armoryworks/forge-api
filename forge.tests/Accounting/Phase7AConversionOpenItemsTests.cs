using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Data;
using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// §7A conversion — the opening AR/AP lines get a sub-ledger: each customer debit on AR control becomes a
/// Sent invoice with an open item, each vendor credit on AP control an Approved bill with an open item, so the
/// agings tie to control and the hard-close checklist can pass. Re-runs and the boot repair never duplicate
/// a document, and the repair skips a party whose opening balance was already re-keyed by hand.
/// </summary>
public class Phase7AConversionOpenItemsTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int CashId = 100;
    private const int ArId = 110;
    private const int ApId = 200;
    private const int ReId = 300;

    private static readonly DateOnly AsOf = new(2026, 1, 1);

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

    private static ConversionService Service(AppDbContext db) => new(db, Engine(db));

    private static async Task<(AppDbContext Db, int Customer, int Vendor)> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        db.Set<FiscalYear>().Add(new FiscalYear { Id = 10, BookId = BookId, Name = "2026", StartDate = AsOf, EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod { Id = 1000, FiscalYearId = 10, PeriodNumber = 1, Name = "2026", StartDate = AsOf, EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ArId, BookId = BookId, AccountNumber = "11000", Name = "Accounts Receivable", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.AR, IsPostable = true, IsActive = true },
            new GlAccount { Id = ApId, BookId = BookId, AccountNumber = "20000", Name = "Accounts Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsControlAccount = true, ControlType = ControlAccountType.AP, IsPostable = true, IsActive = true },
            new GlAccount { Id = ReId, BookId = BookId, AccountNumber = "30000", Name = "Retained Earnings", AccountType = AccountType.Equity, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "AR_CONTROL", GlAccountId = ArId },
            new AccountDeterminationRule { BookId = BookId, Key = "AP_CONTROL", GlAccountId = ApId },
            new AccountDeterminationRule { BookId = BookId, Key = "RETAINED_EARNINGS", GlAccountId = ReId });
        var customer = new Customer { Name = "Customer A", CompanyName = "Customer A", IsActive = true };
        var vendor = new Vendor { CompanyName = "Vendor B", IsActive = true };
        db.Set<Customer>().Add(customer);
        db.Set<Vendor>().Add(vendor);
        await db.SaveChangesAsync();
        return (db, customer.Id, vendor.Id);
    }

    private static PostOpeningBalancesModel Opening(int customerId, int vendorId) => new(BookId, AsOf,
        [
            new OpeningBalanceLineModel(null, ArId, 120000m, 0m, SubledgerPartyType.Customer, customerId, "Open invoice L-1001",
                "L-1001", new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 31)),
            new OpeningBalanceLineModel(null, ArId, 66500m, 0m, SubledgerPartyType.Customer, customerId, "Open invoice L-1002"),
            new OpeningBalanceLineModel(null, ApId, 0m, 71300m, SubledgerPartyType.Vendor, vendorId, "Open bill V-77", "V-77"),
            new OpeningBalanceLineModel(null, ReId, 0m, 115200m, null, null, "Retained earnings offset"),
        ]);

    [Fact]
    public async Task OpeningArAndAp_GetBackingDocumentsAndOpenItems_SoBothAgingsTie()
    {
        var (db, customerId, vendorId) = await SeedAsync();
        using var _ = db;

        await Service(db).PostOpeningBalancesAsync(Opening(customerId, vendorId), postedByUserId: 7);

        var invoices = await db.Invoices.Include(i => i.Lines).OrderBy(i => i.Id).ToListAsync();
        invoices.Should().HaveCount(2);
        invoices.Should().OnlyContain(i => i.Provider == ConversionOpenItems.Provider
            && i.Status == InvoiceStatus.Sent && i.TaxRate == 0m && i.CustomerId == customerId);
        invoices[0].InvoiceNumber.Should().Be("L-1001");
        DateOnly.FromDateTime(invoices[0].InvoiceDate.UtcDateTime).Should().Be(new DateOnly(2025, 12, 1));
        DateOnly.FromDateTime(invoices[0].DueDate.UtcDateTime).Should().Be(new DateOnly(2025, 12, 31));
        invoices[0].BalanceDue.Should().Be(120000m);
        invoices[1].InvoiceNumber.Should().StartWith("INV-");
        DateOnly.FromDateTime(invoices[1].InvoiceDate.UtcDateTime).Should().Be(AsOf);
        invoices[1].BalanceDue.Should().Be(66500m);

        var bill = await db.VendorBills.SingleAsync();
        bill.VendorId.Should().Be(vendorId);
        bill.Status.Should().Be(VendorBillStatus.Approved);
        bill.VendorInvoiceNumber.Should().Be("V-77");
        bill.Provider.Should().Be(ConversionOpenItems.Provider);

        (await db.ArOpenItems.SumAsync(i => i.OriginalFunctionalAmount)).Should().Be(186500m);
        (await db.ApOpenItems.SingleAsync()).OriginalFunctionalAmount.Should().Be(71300m);

        var ar = await new ArAgingService(db, new SystemClock()).ReconcileAsync(BookId, AsOf);
        var ap = await new ApAgingService(db, new SystemClock()).ReconcileAsync(BookId, AsOf);
        ar.IsReconciled.Should().BeTrue($"AR difference {ar.Difference}");
        ap.IsReconciled.Should().BeTrue($"AP difference {ap.Difference}");
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(1, "the documents post nothing");
    }

    [Fact]
    public async Task Rerun_CreatesNoSecondDocument()
    {
        var (db, customerId, vendorId) = await SeedAsync();
        using var _ = db;
        var svc = Service(db);

        await svc.PostOpeningBalancesAsync(Opening(customerId, vendorId), 7);
        await svc.PostOpeningBalancesAsync(Opening(customerId, vendorId), 7);
        await SeedData.SeedAccountingAsync(db);

        (await db.Invoices.CountAsync()).Should().Be(2);
        (await db.VendorBills.CountAsync()).Should().Be(1);
        (await db.ArOpenItems.CountAsync()).Should().Be(2);
        (await db.ApOpenItems.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DocumentNumberAlreadyInUse_IsRefusedBeforeAnythingPosts()
    {
        var (db, customerId, vendorId) = await SeedAsync();
        using var _ = db;
        db.Invoices.Add(new Invoice { InvoiceNumber = "L-1001", CustomerId = customerId, CurrencyId = UsdId });
        await db.SaveChangesAsync();

        var act = async () => await Service(db).PostOpeningBalancesAsync(Opening(customerId, vendorId), 7);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*L-1001*already in use*");
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    private static async Task<long> PostLegacyOpeningWithoutDocumentsAsync(AppDbContext db, int customerId, int vendorId)
    {
        var entry = await Engine(db).PostAsync(new PostingRequest
        {
            BookId = BookId, EntryDate = AsOf, Source = JournalSource.Conversion, SourceType = "OpeningBalance",
            SourceId = BookId, CurrencyId = UsdId, IdempotencyKey = $"{JournalSource.Conversion}:OpeningBalance:{BookId}",
            Lines =
            [
                new PostingLine { GlAccountId = ArId, Debit = 186500m, PartyType = SubledgerPartyType.Customer, PartyId = customerId, Description = "Open invoice L-1001" },
                new PostingLine { GlAccountId = ApId, Credit = 71300m, PartyType = SubledgerPartyType.Vendor, PartyId = vendorId, Description = "Open bill V-77" },
                new PostingLine { GlAccountId = ReId, Credit = 115200m, Description = "offset" },
            ],
        }, 7);
        return entry.Id;
    }

    [Fact]
    public async Task BootRepair_BuildsTheSubledgerForAnOpeningLoadedBeforeTheFix()
    {
        var (db, customerId, vendorId) = await SeedAsync();
        using var _ = db;
        await PostLegacyOpeningWithoutDocumentsAsync(db, customerId, vendorId);

        await SeedData.SeedAccountingAsync(db);

        (await db.ArOpenItems.SingleAsync()).OriginalFunctionalAmount.Should().Be(186500m);
        (await db.ApOpenItems.SingleAsync()).OriginalFunctionalAmount.Should().Be(71300m);
        (await new ArAgingService(db, new SystemClock()).ReconcileAsync(BookId, AsOf)).IsReconciled.Should().BeTrue();
        (await new ApAgingService(db, new SystemClock()).ReconcileAsync(BookId, AsOf)).IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task BootRepair_SkipsAPartyWhoseOpeningBalanceWasAlreadyRekeyed()
    {
        var (db, customerId, vendorId) = await SeedAsync();
        using var _ = db;
        await PostLegacyOpeningWithoutDocumentsAsync(db, customerId, vendorId);
        var rekeyed = new Invoice { InvoiceNumber = "L-1001", CustomerId = customerId, CurrencyId = UsdId, Status = InvoiceStatus.Sent };
        db.Invoices.Add(rekeyed);
        await db.SaveChangesAsync();
        db.ArOpenItems.Add(new ArOpenItem
        {
            BookId = BookId, CustomerId = customerId, SourceType = "Invoice", SourceId = rekeyed.Id, DocumentNumber = "L-1001",
            DocumentDate = new DateTimeOffset(2025, 12, 1, 12, 0, 0, TimeSpan.Zero), CurrencyId = UsdId,
            OriginalTxnAmount = 186500m, OriginalFunctionalAmount = 186500m,
        });
        await db.SaveChangesAsync();

        await SeedData.SeedAccountingAsync(db);

        (await db.ArOpenItems.CountAsync()).Should().Be(1, "the customer's opening balance already has its open item");
        (await db.Invoices.CountAsync()).Should().Be(1);
        (await db.ApOpenItems.CountAsync()).Should().Be(1, "the vendor side still had a gap equal to its opening line");
    }

    [Fact]
    public async Task BootRepair_ToleratesASubCentTaxResidualOnTheParty()
    {
        var (db, customerId, vendorId) = await SeedAsync();
        using var _ = db;
        await PostLegacyOpeningWithoutDocumentsAsync(db, customerId, vendorId);
        var taxed = new Invoice { InvoiceNumber = "INV-00009", CustomerId = customerId, CurrencyId = UsdId, Status = InvoiceStatus.Sent };
        db.Invoices.Add(taxed);
        await db.SaveChangesAsync();
        await Engine(db).PostAsync(new PostingRequest
        {
            BookId = BookId, EntryDate = new DateOnly(2026, 3, 1), Source = JournalSource.AR, SourceType = "Invoice",
            SourceId = taxed.Id, CurrencyId = UsdId, IdempotencyKey = $"AR:Invoice:{taxed.Id}:REVENUE",
            Lines =
            [
                new PostingLine { GlAccountId = ArId, Debit = 13940.355m, PartyType = SubledgerPartyType.Customer, PartyId = customerId, Description = "AR" },
                new PostingLine { GlAccountId = CashId, Credit = 13940.355m, Description = "stand-in for revenue and tax" },
            ],
        }, 7);
        db.ArOpenItems.Add(new ArOpenItem
        {
            BookId = BookId, CustomerId = customerId, SourceType = "Invoice", SourceId = taxed.Id, DocumentNumber = "INV-00009",
            DocumentDate = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero), CurrencyId = UsdId,
            OriginalTxnAmount = 13940.36m, OriginalFunctionalAmount = 13940.36m,
        });
        await db.SaveChangesAsync();

        await SeedData.SeedAccountingAsync(db);

        (await db.ArOpenItems.CountAsync()).Should().Be(2, "the opening line gets its document despite the half-cent residual");
    }
}
