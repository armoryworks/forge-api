using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Phase-1 STAGE B — Payment / cash-receipt posting wired into the CreatePayment
/// flow (ACCOUNTING_SUITE_PLAN §7 matrix row "Payment applied"). Proves:
///   • DARK by default — a no-op while CAP-ACCT-FULLGL is OFF (zero behavior change);
///   • fully-applied payment → Dr CASH / Cr AR_CONTROL (party = customer);
///   • overpayment → unapplied remainder → Cr CUSTOMER_DEPOSITS;
///   • a pure on-account (no applications) payment → all to CUSTOMER_DEPOSITS;
///   • idempotent — a re-post returns the existing entry (no duplicate).
/// </summary>
public class PaymentCashPostingServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int EurId = 2; // 2nd currency for the realized-FX coverage
    private const int FiscalYearId = 10;

    private const int CashId = 100;
    private const int ArControlId = 102;
    private const int CustomerDepositsId = 105;
    private const int FxGainId = 106; // Phase-4 — realized FX gain
    private const int FxLossId = 107; // Phase-4 — realized FX loss

    private const int OpenPeriodId = 1000;

    /// <summary>In-process allocator (InMemory can't run the row-lock SQL).</summary>
    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    /// <summary>Toggleable capability snapshot provider for the FULLGL gate.</summary>
    private sealed class FakeCapabilities(bool fullGlOn) : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["CAP-ACCT-FULLGL"] = fullGlOn },
            DateTimeOffset.UtcNow);

        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static PaymentCashPostingService CreateService(AppDbContext db, bool fullGlOn)
        => new(
            db,
            new ForgeGlPostingEngine(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock()),
            new FakeCapabilities(fullGlOn));

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();

        db.Set<Currency>().AddRange(
            new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" },
            new Currency { Id = EurId, Code = "EUR", Name = "Euro", Symbol = "€" });

        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
            RevenueRecognitionMethod = RevenueRecognitionMethod.PointInTime,
        });

        var fy = new FiscalYear
        {
            Id = FiscalYearId, BookId = BookId, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            Status = FiscalYearStatus.Open,
        };
        db.Set<FiscalYear>().Add(fy);
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "Jan 2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31),
            Status = FiscalPeriodStatus.Open,
        });

        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ArControlId, BookId = BookId, AccountNumber = "11000", Name = "Accounts Receivable", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.AR, IsPostable = true, IsActive = true },
            new GlAccount { Id = CustomerDepositsId, BookId = BookId, AccountNumber = "24500", Name = "Customer Deposits", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = FxGainId, BookId = BookId, AccountNumber = "90000", Name = "Foreign Exchange Gain", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = FxLossId, BookId = BookId, AccountNumber = "90100", Name = "Foreign Exchange Loss", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });

        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "AR_CONTROL", GlAccountId = ArControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "CUSTOMER_DEPOSITS", GlAccountId = CustomerDepositsId },
            new AccountDeterminationRule { BookId = BookId, Key = "FX_GAIN", GlAccountId = FxGainId },
            new AccountDeterminationRule { BookId = BookId, Key = "FX_LOSS", GlAccountId = FxLossId });

        await db.SaveChangesAsync();
        return db;
    }

    /// <summary>
    /// Adds a customer + payment (with optional applications). The applications
    /// reference real invoice ids only nominally — the cash posting reads applied
    /// vs unapplied amounts off the Payment, not the invoices.
    /// </summary>
    private static async Task<Payment> AddPaymentAsync(
        AppDbContext db,
        decimal amount,
        IEnumerable<decimal>? applicationAmounts = null)
    {
        var customer = new Customer { Name = "Acme Corp" };
        db.Set<Customer>().Add(customer);
        await db.SaveChangesAsync();

        var payment = new Payment
        {
            PaymentNumber = "PMT-1001",
            CustomerId = customer.Id,
            Method = PaymentMethod.Check,
            Amount = amount,
            PaymentDate = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
        };

        if (applicationAmounts is not null)
        {
            var n = 0;
            foreach (var appAmount in applicationAmounts)
            {
                n++;
                payment.Applications.Add(new PaymentApplication { InvoiceId = n, Amount = appAmount });
            }
        }

        db.Set<Payment>().Add(payment);
        await db.SaveChangesAsync();
        return payment;
    }

    /// <summary>
    /// Adds a customer, a real invoice (in <paramref name="invoiceCurrencyId"/> at booking
    /// <paramref name="invoiceFxRate"/>), and a payment applying <paramref name="appliedForeign"/> of the
    /// invoice's foreign amount at settlement rate <paramref name="settlementFxRate"/>. The realized-FX
    /// posting loads the invoice for its booking rate, so the application must reference a real invoice id.
    /// </summary>
    private static async Task<Payment> AddPaymentForInvoiceAsync(
        AppDbContext db,
        decimal appliedForeign,
        int invoiceCurrencyId,
        decimal invoiceFxRate,
        decimal settlementFxRate,
        decimal? paymentAmountFunctional = null)
    {
        var customer = new Customer { Name = "Acme Corp" };
        db.Set<Customer>().Add(customer);
        await db.SaveChangesAsync();

        var invoice = new Invoice
        {
            InvoiceNumber = "INV-FX-1",
            CustomerId = customer.Id,
            CurrencyId = invoiceCurrencyId,
            FxRate = invoiceFxRate,
            InvoiceDate = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 2, 9, 0, 0, 0, TimeSpan.Zero),
            Status = InvoiceStatus.Sent,
            TaxRate = 0m,
            Lines = [new InvoiceLine { Description = "Widget", Quantity = 1m, UnitPrice = appliedForeign, LineNumber = 1 }],
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        // Payment.Amount is functional cash. Default = the application's cash leg (foreign × settlement), i.e.
        // fully applied with no unapplied remainder.
        var amount = paymentAmountFunctional
            ?? Math.Round(appliedForeign * settlementFxRate, 2, MidpointRounding.AwayFromZero);

        var payment = new Payment
        {
            PaymentNumber = "PMT-FX-1",
            CustomerId = customer.Id,
            Method = PaymentMethod.Check,
            Amount = amount,
            PaymentDate = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            Applications = [new PaymentApplication { InvoiceId = invoice.Id, Amount = appliedForeign, SettlementFxRate = settlementFxRate }],
        };
        db.Set<Payment>().Add(payment);
        await db.SaveChangesAsync();
        return payment;
    }

    [Fact]
    public async Task Post_WhenFullGlOff_IsNoOp()
    {
        using var db = await SeedAsync();
        var payment = await AddPaymentAsync(db, amount: 100m, applicationAmounts: [100m]);
        var service = CreateService(db, fullGlOn: false);

        await service.PostPaymentCreatedAsync(payment.Id, createdByUserId: 7);

        // Dark by default: nothing posted, no ledger movement at all.
        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
        (await db.LedgerBalances.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Post_WhenFullGlOn_FullyApplied_PostsCashAndAr()
    {
        using var db = await SeedAsync();
        var payment = await AddPaymentAsync(db, amount: 100m, applicationAmounts: [100m]);
        var service = CreateService(db, fullGlOn: true);

        await service.PostPaymentCreatedAsync(payment.Id, createdByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.Source.Should().Be(JournalSource.AR);
        entry.SourceType.Should().Be("Payment");
        entry.SourceId.Should().Be(payment.Id);
        entry.EntryDate.Should().Be(new DateOnly(2026, 1, 15));
        entry.Status.Should().Be(JournalEntryStatus.Posted);
        entry.PostedBy.Should().Be(7);

        // Dr Cash for the full amount.
        entry.Lines.Single(l => l.GlAccountId == CashId).Debit.Should().Be(100m);

        // Cr AR for the applied amount, party = customer.
        var ar = entry.Lines.Single(l => l.GlAccountId == ArControlId);
        ar.Credit.Should().Be(100m);
        ar.SubledgerPartyType.Should().Be(SubledgerPartyType.Customer);
        ar.SubledgerPartyId.Should().Be(payment.CustomerId);

        // Fully applied → no customer-deposit line.
        entry.Lines.Should().NotContain(l => l.GlAccountId == CustomerDepositsId);

        // Balanced.
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Post_WhenFullGlOn_Overpayment_BooksUnappliedToCustomerDeposits()
    {
        using var db = await SeedAsync();
        // Pays 150, applies 100 to an invoice → 50 unapplied.
        var payment = await AddPaymentAsync(db, amount: 150m, applicationAmounts: [100m]);
        var service = CreateService(db, fullGlOn: true);

        await service.PostPaymentCreatedAsync(payment.Id, createdByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();

        entry.Lines.Single(l => l.GlAccountId == CashId).Debit.Should().Be(150m);
        entry.Lines.Single(l => l.GlAccountId == ArControlId).Credit.Should().Be(100m);
        entry.Lines.Single(l => l.GlAccountId == CustomerDepositsId).Credit.Should().Be(50m);

        // Balanced: Dr 150 == Cr 100 + 50.
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Post_WhenFullGlOn_OnAccount_NoApplications_AllToCustomerDeposits()
    {
        using var db = await SeedAsync();
        // No applications → entire amount is unapplied (on-account deposit).
        var payment = await AddPaymentAsync(db, amount: 100m, applicationAmounts: null);
        var service = CreateService(db, fullGlOn: true);

        await service.PostPaymentCreatedAsync(payment.Id, createdByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();

        entry.Lines.Single(l => l.GlAccountId == CashId).Debit.Should().Be(100m);
        entry.Lines.Single(l => l.GlAccountId == CustomerDepositsId).Credit.Should().Be(100m);
        // Nothing applied → no AR relief line.
        entry.Lines.Should().NotContain(l => l.GlAccountId == ArControlId);
    }

    // ─────────────────────────── Realized FX at settlement (Phase-4) ───────────────────────────

    [Fact]
    public async Task Post_RealizedFxLoss_DebitsFxLoss_ArNetsToZero_Balances()
    {
        // EUR invoice foreign 100 booked @1.10 → AR carrying 110. Settled @1.05 → cash 105.
        // Plug +5 → Dr FX_LOSS. Dr Cash 105 / Dr FX_LOSS 5 / Cr AR 110.
        using var db = await SeedAsync();
        var payment = await AddPaymentForInvoiceAsync(db, appliedForeign: 100m, invoiceCurrencyId: EurId, invoiceFxRate: 1.10m, settlementFxRate: 1.05m);

        await CreateService(db, fullGlOn: true).PostPaymentCreatedAsync(payment.Id, createdByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == CashId).Debit.Should().Be(105m);
        entry.Lines.Single(l => l.GlAccountId == FxLossId).Debit.Should().Be(5m);

        // AR relieved at the booked carrying value (110) → fully nets the invoice's AR to zero.
        var ar = entry.Lines.Single(l => l.GlAccountId == ArControlId);
        ar.Credit.Should().Be(110m);
        ar.SubledgerPartyType.Should().Be(SubledgerPartyType.Customer);
        ar.SubledgerPartyId.Should().Be(payment.CustomerId);

        // No FX gain on a loss; settlement entry is fully functional (FxRate 1).
        entry.Lines.Should().NotContain(l => l.GlAccountId == FxGainId);
        entry.Lines.Should().OnlyContain(l => l.FxRate == 1m);

        // BALANCES: Dr 105 + 5 == Cr 110.
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Post_RealizedFxGain_CreditsFxGain_ArNetsToZero_Balances()
    {
        // EUR invoice foreign 100 booked @1.10 → AR carrying 110. Settled @1.15 → cash 115.
        // Plug −5 → Cr FX_GAIN. Dr Cash 115 / Cr AR 110 / Cr FX_GAIN 5.
        using var db = await SeedAsync();
        var payment = await AddPaymentForInvoiceAsync(db, appliedForeign: 100m, invoiceCurrencyId: EurId, invoiceFxRate: 1.10m, settlementFxRate: 1.15m);

        await CreateService(db, fullGlOn: true).PostPaymentCreatedAsync(payment.Id, createdByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == CashId).Debit.Should().Be(115m);
        entry.Lines.Single(l => l.GlAccountId == ArControlId).Credit.Should().Be(110m);
        entry.Lines.Single(l => l.GlAccountId == FxGainId).Credit.Should().Be(5m);
        entry.Lines.Should().NotContain(l => l.GlAccountId == FxLossId);

        // BALANCES: Dr 115 == Cr 110 + 5.
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Post_FunctionalCurrencyInvoice_NoFxLine_BackwardCompatible()
    {
        // USD (functional) invoice booked @1, settled @1 → arRelief == cash == applied → NO FX line.
        // The entry is the SAME shape as the pre-FX single-currency path: Dr Cash 100 / Cr AR 100.
        using var db = await SeedAsync();
        var payment = await AddPaymentForInvoiceAsync(db, appliedForeign: 100m, invoiceCurrencyId: UsdId, invoiceFxRate: 1m, settlementFxRate: 1m);

        await CreateService(db, fullGlOn: true).PostPaymentCreatedAsync(payment.Id, createdByUserId: 7);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == CashId).Debit.Should().Be(100m);
        entry.Lines.Single(l => l.GlAccountId == ArControlId).Credit.Should().Be(100m);
        // No FX line at all — byte-identical to today.
        entry.Lines.Should().NotContain(l => l.GlAccountId == FxGainId || l.GlAccountId == FxLossId);
        entry.Lines.Should().HaveCount(2);
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Post_CalledTwice_IsIdempotent()
    {
        using var db = await SeedAsync();
        var payment = await AddPaymentAsync(db, amount: 100m, applicationAmounts: [100m]);
        var service = CreateService(db, fullGlOn: true);

        await service.PostPaymentCreatedAsync(payment.Id, createdByUserId: 7);
        await service.PostPaymentCreatedAsync(payment.Id, createdByUserId: 7);

        // A re-post returns the existing entry — no duplicate journal.
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    // ─────────────────────── AR-002 — open-item sub-ledger maintenance ───────────────────────

    [Fact]
    public async Task Post_IncrementsArOpenItem_ByBookingRateRelief()
    {
        using var db = await SeedAsync();
        // EUR invoice foreign 100 booked @1.10 with its open item (as InvoiceArPostingService creates it).
        var payment = await AddPaymentForInvoiceAsync(
            db, appliedForeign: 100m, invoiceCurrencyId: EurId, invoiceFxRate: 1.10m, settlementFxRate: 1.05m);
        var invoiceId = payment.Applications.Single().InvoiceId;
        db.ArOpenItems.Add(new ArOpenItem
        {
            BookId = BookId, CustomerId = payment.CustomerId, SourceType = "Invoice", SourceId = invoiceId,
            DocumentNumber = "INV-FX-1", DocumentDate = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 2, 9, 0, 0, 0, TimeSpan.Zero), CurrencyId = EurId, FxRate = 1.10m,
            OriginalTxnAmount = 100m, OriginalFunctionalAmount = 110m, Status = OpenItemStatus.Open,
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, fullGlOn: true);
        await service.PostPaymentCreatedAsync(payment.Id, createdByUserId: 7);

        // Applied at the BOOKING-rate relief (110), not the settlement-rate cash (105); exact-close → Closed.
        var item = await db.ArOpenItems.SingleAsync();
        item.AppliedTxnAmount.Should().Be(100m);
        item.AppliedFunctionalAmount.Should().Be(110m);
        item.Status.Should().Be(OpenItemStatus.Closed);

        // A re-post never double-applies (the journal de-dupe guard covers the item maintenance too).
        await service.PostPaymentCreatedAsync(payment.Id, createdByUserId: 7);
        (await db.ArOpenItems.SingleAsync()).AppliedTxnAmount.Should().Be(100m);
    }
}
