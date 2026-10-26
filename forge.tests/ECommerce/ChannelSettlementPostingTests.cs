using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.ECommerce;
using Forge.Api.Features.Payments;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.ECommerce;

public class ChannelSettlementPostingTests : IDisposable
{
    private const int BookId = 1;
    private const string FullGl = "CAP-ACCT-FULLGL";
    private const int CashId = 101;
    private const int ArId = 110;
    private const int TaxId = 230;
    private const int DepositsId = 245;
    private const int RevenueId = 400;
    private const int ReturnsId = 410;
    private const int FeesId = 615;
    private const int ReserveId = 125;

    private static readonly DateTimeOffset October = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private int _houseCustomerId;
    private int _channelId;

    private sealed class Allocator(AppDbContext db) : IAcctNumberSequenceAllocator
    {
        public async Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => (await db.JournalEntries.IgnoreQueryFilters()
                .Where(e => e.BookId == bookId && e.FiscalYearId == fiscalYearId)
                .MaxAsync(e => (long?)e.EntryNumber, ct) ?? 0) + 1;
    }

    private ForgeGlPostingEngine Engine() =>
        new(_db, new AccountDeterminationResolver(_db), new Allocator(_db), new SystemClock());

    private ChannelSettlementPostingService Posting(bool fullGlOn = true)
    {
        var capabilities = fullGlOn ? new StubCapabilitySnapshotProvider(FullGl) : StubCapabilitySnapshotProvider.Off;
        return new(
            _db, Engine(), capabilities,
            new PaymentCashPostingService(_db, Engine(), capabilities),
            new PaymentRepository(_db));
    }

    private async Task SeedAsync()
    {
        _db.Set<Currency>().Add(new Currency { Id = 1, Code = "USD", Name = "US Dollar", Symbol = "$" });
        _db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = 1,
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
            Id = 1000, FiscalYearId = 10, PeriodNumber = 1, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open,
        });
        _db.Set<GlAccount>().AddRange(
            Account(CashId, "10100", "Cash", AccountType.Asset, NormalBalance.Debit),
            new GlAccount { Id = ArId, BookId = BookId, AccountNumber = "11000", Name = "Accounts Receivable", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.AR, IsPostable = true, IsActive = true },
            Account(ReserveId, "12500", "Marketplace Reserves Held", AccountType.Asset, NormalBalance.Debit),
            Account(TaxId, "23000", "Sales Tax Payable", AccountType.Liability, NormalBalance.Credit),
            Account(DepositsId, "24500", "Customer Deposits", AccountType.Liability, NormalBalance.Credit),
            Account(RevenueId, "40000", "Sales Revenue", AccountType.Income, NormalBalance.Credit),
            Account(ReturnsId, "41000", "Sales Returns", AccountType.Income, NormalBalance.Debit),
            Account(FeesId, "61500", "Marketplace Fees", AccountType.Expense, NormalBalance.Debit));
        _db.Set<AccountDeterminationRule>().AddRange(
            Rule("CASH", CashId), Rule("AR_CONTROL", ArId), Rule("MARKETPLACE_RESERVE", ReserveId),
            Rule("SALES_TAX_PAYABLE", TaxId), Rule("CUSTOMER_DEPOSITS", DepositsId), Rule("SALES_REVENUE", RevenueId),
            Rule("DEFERRED_REVENUE", RevenueId), Rule("SALES_RETURNS", ReturnsId), Rule("MARKETPLACE_FEES", FeesId));

        var house = new Customer { Name = "Marketplace house account" };
        _db.Customers.Add(house);
        await _db.SaveChangesAsync();
        _houseCustomerId = house.Id;

        var channel = new SalesChannel
        {
            Name = "Marketplace", Code = "MKT", ChannelType = SalesChannelType.Marketplace,
            SoldToCustomerId = house.Id, TaxCollectedBy = TaxCollectedBy.Marketplace,
            ECommerceIntegration = new ECommerceIntegration { Name = "Mock Amazon", Platform = ECommercePlatform.Amazon, EncryptedCredentials = "x" },
        };
        _db.SalesChannels.Add(channel);
        await _db.SaveChangesAsync();
        _channelId = channel.Id;
    }

    private static GlAccount Account(int id, string number, string name, AccountType type, NormalBalance normal) =>
        new() { Id = id, BookId = BookId, AccountNumber = number, Name = name, AccountType = type, NormalBalance = normal, IsPostable = true, IsActive = true };

    private static AccountDeterminationRule Rule(string key, int accountId) =>
        new() { BookId = BookId, Key = key, GlAccountId = accountId };

    private async Task<SalesOrder> AddInvoicedOrderAsync(string externalId, bool invoice = true)
    {
        var order = new SalesOrder
        {
            OrderNumber = $"MKT-{externalId}",
            CustomerId = _houseCustomerId,
            ChannelId = _channelId,
            ExternalId = externalId,
            Status = SalesOrderStatus.Confirmed,
            TaxRate = 0.10m,
            TaxCollectedBy = TaxCollectedBy.Marketplace,
        };
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();
        if (!invoice)
            return order;

        var inv = new Invoice
        {
            InvoiceNumber = $"INV-{externalId}"[..Math.Min(20, 4 + externalId.Length)],
            CustomerId = _houseCustomerId,
            SalesOrderId = order.Id,
            CurrencyId = 1,
            Status = InvoiceStatus.Sent,
            TaxRate = 0.10m,
            InvoiceDate = October,
            DueDate = October.AddDays(30),
        };
        inv.Lines.Add(new InvoiceLine { Description = "Widget", Quantity = 1m, UnitPrice = 100m, LineNumber = 1 });
        _db.Invoices.Add(inv);
        await _db.SaveChangesAsync();
        await new InvoiceArPostingService(_db, Engine(), new StubCapabilitySnapshotProvider(FullGl))
            .PostInvoiceFinalizedAsync(inv.Id, 1);
        return order;
    }

    private async Task<ChannelSettlement> AddSettlementAsync(
        int? orderId, decimal reported, ChannelSettlementStatus status, params (ChannelSettlementLineType Type, decimal Amount)[] lines)
    {
        var settlement = new ChannelSettlement
        {
            ChannelId = _channelId,
            ExternalSettlementId = $"STL-{Guid.NewGuid():N}",
            PeriodStart = October,
            PeriodEnd = October.AddDays(14),
            DepositedAt = October.AddDays(16),
            ReportedNetAmount = reported,
            CurrencyCode = "USD",
            Status = status,
        };
        foreach (var (type, amount) in lines)
            settlement.Lines.Add(new ChannelSettlementLine
            {
                LineType = type,
                Amount = amount,
                SalesOrderId = type is ChannelSettlementLineType.OrderProceeds ? orderId : null,
                ExternalOrderId = "EXT",
            });
        _db.ChannelSettlements.Add(settlement);
        await _db.SaveChangesAsync();
        return settlement;
    }

    private async Task<JournalEntry> SettlementEntryAsync(int settlementId) =>
        await _db.JournalEntries.Include(e => e.Lines)
            .SingleAsync(e => e.SourceType == "ChannelSettlement" && e.SourceId == settlementId);

    private static decimal Net(JournalEntry entry, int accountId) =>
        entry.Lines.Where(l => l.GlAccountId == accountId).Sum(l => l.Debit - l.Credit);

    private async Task<decimal> LedgerNetAsync(int accountId) =>
        await _db.JournalLines
            .Where(l => l.GlAccountId == accountId)
            .SumAsync(l => l.Debit - l.Credit);

    private async Task<JournalEntry> PayoutPaymentEntryAsync(int paymentId) =>
        await _db.JournalEntries.Include(e => e.Lines)
            .SingleAsync(e => e.SourceType == "Payment" && e.SourceId == paymentId);

    [Fact]
    public async Task Reconciled_payout_posts_cash_fees_withheld_tax_and_relieves_the_house_account()
    {
        await SeedAsync();
        var order = await AddInvoicedOrderAsync("A1");
        var settlement = await AddSettlementAsync(order.Id, 88m, ChannelSettlementStatus.Reconciled,
            (ChannelSettlementLineType.OrderProceeds, 110m),
            (ChannelSettlementLineType.ReferralFee, -12m),
            (ChannelSettlementLineType.TaxCollected, -10m));

        await Posting().PostSettlementAsync(settlement.Id, 1);
        await Posting().PostSettlementAsync(settlement.Id, 1);

        var entry = await SettlementEntryAsync(settlement.Id);
        Net(entry, CashId).Should().Be(-22m);
        Net(entry, FeesId).Should().Be(12m);
        Net(entry, TaxId).Should().Be(10m);
        entry.Lines.Should().NotContain(l => l.GlAccountId == ArId || l.GlAccountId == DepositsId);

        var payment = await _db.Payments.Include(p => p.Applications).SingleAsync();
        payment.CustomerId.Should().Be(_houseCustomerId);
        payment.Method.Should().Be(PaymentMethod.BankTransfer);
        payment.Amount.Should().Be(110m);
        payment.PaymentDate.Should().Be(settlement.DepositedAt!.Value);
        payment.Applications.Should().ContainSingle().Which.Amount.Should().Be(110m);
        var paymentEntry = await PayoutPaymentEntryAsync(payment.Id);
        Net(paymentEntry, CashId).Should().Be(110m);
        Net(paymentEntry, ArId).Should().Be(-110m);
        paymentEntry.Lines.Single(l => l.GlAccountId == ArId).SubledgerPartyId.Should().Be(_houseCustomerId);
        (await LedgerNetAsync(CashId)).Should().Be(88m);

        (await _db.Invoices.SingleAsync()).Status.Should().Be(InvoiceStatus.Paid);
        var openItem = await _db.ArOpenItems.SingleAsync();
        openItem.OpenTxnAmount.Should().Be(0m);
        openItem.Status.Should().Be(OpenItemStatus.Closed);
        (await _db.JournalEntries.CountAsync(e => e.SourceType == "ChannelSettlement")).Should().Be(1);
        (await _db.Payments.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Proceeds_for_an_order_not_yet_invoiced_go_to_customer_deposits()
    {
        await SeedAsync();
        var order = await AddInvoicedOrderAsync("B1", invoice: false);
        var settlement = await AddSettlementAsync(order.Id, 95m, ChannelSettlementStatus.Reconciled,
            (ChannelSettlementLineType.OrderProceeds, 100m),
            (ChannelSettlementLineType.FulfillmentFee, -5m));

        await Posting().PostSettlementAsync(settlement.Id, 1);

        var entry = await SettlementEntryAsync(settlement.Id);
        Net(entry, CashId).Should().Be(95m);
        Net(entry, FeesId).Should().Be(5m);
        Net(entry, DepositsId).Should().Be(-100m);
        entry.Lines.Should().NotContain(l => l.GlAccountId == ArId);
    }

    [Fact]
    public async Task Accepted_variance_refunds_and_reserves_land_on_their_own_accounts()
    {
        await SeedAsync();
        var order = await AddInvoicedOrderAsync("C1");
        var settlement = await AddSettlementAsync(order.Id, 60m, ChannelSettlementStatus.Accepted,
            (ChannelSettlementLineType.OrderProceeds, 110m),
            (ChannelSettlementLineType.TaxCollected, -10m),
            (ChannelSettlementLineType.Refund, -20m),
            (ChannelSettlementLineType.ReserveAdjustment, -15m));

        await Posting().PostSettlementAsync(settlement.Id, 1);

        var entry = await SettlementEntryAsync(settlement.Id);
        Net(entry, CashId).Should().Be(-50m);
        Net(entry, ReturnsId).Should().Be(20m);
        Net(entry, ReserveId).Should().Be(15m);
        Net(entry, FeesId).Should().Be(5m);
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
        (await LedgerNetAsync(CashId)).Should().Be(60m);
        (await LedgerNetAsync(ArId)).Should().Be(0m);
    }

    [Fact]
    public async Task Nothing_posts_while_built_in_accounting_is_off()
    {
        await SeedAsync();
        var order = await AddInvoicedOrderAsync("D1");
        var settlement = await AddSettlementAsync(order.Id, 110m, ChannelSettlementStatus.Reconciled,
            (ChannelSettlementLineType.OrderProceeds, 110m));

        await Posting(fullGlOn: false).PostSettlementAsync(settlement.Id, 1);

        (await _db.JournalEntries.AnyAsync(e => e.SourceType == "ChannelSettlement")).Should().BeFalse();
        (await _db.Payments.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Accepting_a_variance_posts_the_batch_once()
    {
        await SeedAsync();
        var order = await AddInvoicedOrderAsync("E1");
        var settlement = await AddSettlementAsync(order.Id, 90m, ChannelSettlementStatus.Discrepancy,
            (ChannelSettlementLineType.OrderProceeds, 110m),
            (ChannelSettlementLineType.TaxCollected, -10m),
            (ChannelSettlementLineType.ReferralFee, -12m));
        var handler = new AcceptChannelSettlementHandler(_db, Posting());

        await handler.Handle(new AcceptChannelSettlementCommand(settlement.Id, "Reserve released"), default);
        await handler.Handle(new AcceptChannelSettlementCommand(settlement.Id, "Reserve released"), default);

        var entry = await SettlementEntryAsync(settlement.Id);
        Net(entry, CashId).Should().Be(-20m);
        Net(entry, FeesId).Should().Be(10m);
        (await LedgerNetAsync(CashId)).Should().Be(90m);
        (await _db.Payments.CountAsync()).Should().Be(1);
        (await _db.ChannelSettlements.SingleAsync()).Status.Should().Be(ChannelSettlementStatus.Accepted);
    }

    [Fact]
    public async Task First_settlement_import_matches_the_orders_the_first_order_import_produced()
    {
        await SeedAsync();
        var now = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
        var connector = new MockECommerceService(ECommercePlatform.Amazon, NullLogger<MockECommerceService>.Instance);
        var orders = await connector.PollOrdersAsync("x", "", now.AddDays(-30), default);
        foreach (var polledOrder in orders)
            await AddInvoicedOrderAsync(polledOrder.ExternalId, invoice: false);

        var factory = new Mock<IECommerceServiceFactory>();
        factory.Setup(f => f.For(ECommercePlatform.Amazon)).Returns(connector);
        var protector = new Mock<IECommerceCredentialProtector>();
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(now);
        var handler = new ImportChannelSettlementsHandler(
            _db, factory.Object, protector.Object, clock.Object, Posting(),
            NullLogger<ImportChannelSettlementsHandler>.Instance);

        var result = await handler.Handle(new ImportChannelSettlementsCommand(_channelId), default);
        var again = await handler.Handle(new ImportChannelSettlementsCommand(_channelId), default);

        result.Imported.Should().Be(1);
        result.UnmatchedOrderLines.Should().Be(0);
        result.Reconciled.Should().Be(1);
        again.Updated.Should().Be(0);
        var settlement = await _db.ChannelSettlements.SingleAsync();
        settlement.Status.Should().Be(ChannelSettlementStatus.Reconciled);
        var entry = await SettlementEntryAsync(settlement.Id);
        Net(entry, CashId).Should().Be(94.13m);
        Net(entry, FeesId).Should().Be(12.35m);
        Net(entry, TaxId).Should().Be(8.25m);
        Net(entry, DepositsId).Should().Be(-114.73m);
    }

    [Fact]
    public async Task A_payout_that_only_settles_invoices_is_not_paid_twice()
    {
        await SeedAsync();
        var order = await AddInvoicedOrderAsync("F1");
        var settlement = await AddSettlementAsync(order.Id, 110m, ChannelSettlementStatus.Reconciled,
            (ChannelSettlementLineType.OrderProceeds, 110m));

        await Posting().PostSettlementAsync(settlement.Id, 1);
        await Posting().PostSettlementAsync(settlement.Id, 1);

        (await _db.JournalEntries.AnyAsync(e => e.SourceType == "ChannelSettlement")).Should().BeFalse();
        (await _db.Payments.CountAsync()).Should().Be(1);
        (await Posting().PostedSettlementIdsAsync([settlement.Id])).Should().Contain(settlement.Id);
        (await LedgerNetAsync(CashId)).Should().Be(110m);
        (await LedgerNetAsync(ArId)).Should().Be(0m);
    }

    [Fact]
    public async Task Voiding_the_payout_payment_reopens_the_invoice_and_restores_AR()
    {
        await SeedAsync();
        var order = await AddInvoicedOrderAsync("G1");
        var settlement = await AddSettlementAsync(order.Id, 98m, ChannelSettlementStatus.Reconciled,
            (ChannelSettlementLineType.OrderProceeds, 110m),
            (ChannelSettlementLineType.ReferralFee, -12m));
        await Posting().PostSettlementAsync(settlement.Id, 1);
        var payment = await _db.Payments.SingleAsync();

        var settings = new Mock<Forge.Core.Settings.ISettingsService>();
        settings.Setup(s => s.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var capabilities = new StubCapabilitySnapshotProvider(FullGl);
        await new VoidPaymentHandler(
                new PaymentRepository(_db), _db, settings.Object,
                new PaymentCashPostingService(_db, Engine(), capabilities))
            .Handle(new VoidPaymentCommand(payment.Id, new VoidPaymentRequestModel("Posted to the wrong batch")), default);

        (await _db.Invoices.SingleAsync()).Status.Should().NotBe(InvoiceStatus.Paid);
        (await _db.ArOpenItems.SingleAsync()).OpenTxnAmount.Should().Be(110m);
        (await LedgerNetAsync(ArId)).Should().Be(110m);
    }

    public void Dispose() => _db.Dispose();
}
