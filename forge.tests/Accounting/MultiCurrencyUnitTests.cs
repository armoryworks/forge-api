using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.Admin;
using Forge.Api.Features.Invoices;
using Forge.Api.Features.Payments;
using Forge.Api.Features.PurchaseOrders;
using Forge.Api.Features.Quotes;
using Forge.Api.Features.VendorBills;
using Forge.Api.Services.Fx;
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

namespace Forge.Tests.Accounting;

/// <summary>
/// Multi-currency rules on the InMemory provider: the rate lookup (direct, latest on or before the date,
/// inverse, triangulated, missing), document currency resolution and inheritance, payment currency checks,
/// the party currency lock and the engine's functional-currency rounding.
/// </summary>
public class MultiCurrencyUnitTests
{
    private const int UsdId = 1;
    private const int CadId = 2;
    private const int EurId = 3;
    private const int GbpId = 4;

    private static readonly DateTimeOffset InvoiceDate = new(2026, 10, 26, 15, 0, 0, TimeSpan.Zero);

    private static AppDbContext Seed()
    {
        var db = TestDbContextFactory.Create();
        db.Set<Currency>().AddRange(
            new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$", IsBaseCurrency = true, IsActive = true },
            new Currency { Id = CadId, Code = "CAD", Name = "Canadian Dollar", Symbol = "$", IsActive = true },
            new Currency { Id = EurId, Code = "EUR", Name = "Euro", Symbol = "€", IsActive = true },
            new Currency { Id = GbpId, Code = "GBP", Name = "Pound", Symbol = "£", IsActive = false });
        db.Set<Book>().Add(new Book
        {
            Id = 1, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        db.Set<ExchangeRate>().AddRange(
            Rate(CadId, UsdId, 0.70m, new DateOnly(2026, 10, 1)),
            Rate(CadId, UsdId, 0.73m, new DateOnly(2026, 10, 20)),
            Rate(CadId, UsdId, 0.80m, new DateOnly(2026, 10, 30)),
            Rate(UsdId, EurId, 0.80m, new DateOnly(2026, 10, 1)));
        db.SaveChanges();
        return db;
    }

    private static ExchangeRate Rate(int from, int to, decimal rate, DateOnly date)
        => new() { FromCurrencyId = from, ToCurrencyId = to, Rate = rate, EffectiveDate = date, Source = ExchangeRateSource.Manual };

    [Fact]
    public async Task Lookup_TakesTheLatestDirectRateOnOrBeforeTheDate()
    {
        using var db = Seed();

        var quote = await new DocumentCurrencyResolver(db).GetRateAsync(CadId, UsdId, new DateOnly(2026, 10, 26), CancellationToken.None);

        quote.Rate.Should().Be(0.73m);
        quote.EffectiveDate.Should().Be(new DateOnly(2026, 10, 20));
        quote.Source.Should().Be("direct");
    }

    [Fact]
    public async Task Lookup_InvertsTheReversePair_WhenNoDirectRow()
    {
        using var db = Seed();

        var quote = await new DocumentCurrencyResolver(db).GetRateAsync(EurId, UsdId, new DateOnly(2026, 10, 26), CancellationToken.None);

        quote.Rate.Should().Be(1.25m);
        quote.Source.Should().Be("inverse");
    }

    [Fact]
    public async Task Lookup_TriangulatesThroughTheFunctionalCurrency()
    {
        using var db = Seed();

        var quote = await new DocumentCurrencyResolver(db).GetRateAsync(CadId, EurId, new DateOnly(2026, 10, 26), CancellationToken.None);

        quote.Rate.Should().Be(0.584m);
        quote.Source.Should().Be("triangulated");
        quote.EffectiveDate.Should().Be(new DateOnly(2026, 10, 1));
    }

    [Fact]
    public async Task Lookup_SameCurrency_IsOne()
    {
        using var db = Seed();

        var quote = await new DocumentCurrencyResolver(db).GetRateAsync(CadId, CadId, new DateOnly(2026, 10, 26), CancellationToken.None);

        quote.Rate.Should().Be(1m);
        quote.Source.Should().Be("same-currency");
    }

    [Fact]
    public async Task Lookup_NoRateOnOrBeforeTheDate_ThrowsFxRateMissing()
    {
        using var db = Seed();

        var act = () => new DocumentCurrencyResolver(db).GetRateAsync(CadId, UsdId, new DateOnly(2026, 9, 30), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<FxRateMissingException>()).Which;
        ex.FromCode.Should().Be("CAD");
        ex.ToCode.Should().Be("USD");
        ex.Message.Should().Be("No exchange rate for CAD to USD on or before 2026-09-30. Add one under Admin > Currencies.");
    }

    [Fact]
    public async Task Resolve_ForeignWithoutRate_LooksUpTheDocumentDate()
    {
        using var db = Seed();

        var currency = await new DocumentCurrencyResolver(db).ResolveAsync(null, null, CadId, InvoiceDate, CancellationToken.None);

        currency.CurrencyId.Should().Be(CadId);
        currency.FxRate.Should().Be(0.73m);
        currency.IsFunctional.Should().BeFalse();
        currency.Code.Should().Be("CAD");
    }

    [Fact]
    public async Task Resolve_ExplicitRateWins_AndFunctionalAlwaysBooksAtOne()
    {
        using var db = Seed();
        var resolver = new DocumentCurrencyResolver(db);

        (await resolver.ResolveAsync(CadId, 1m, null, InvoiceDate, CancellationToken.None)).FxRate.Should().Be(1m);
        (await resolver.ResolveAsync(CadId, 0.7412m, null, InvoiceDate, CancellationToken.None)).FxRate.Should().Be(0.7412m);
        (await resolver.ResolveAsync(UsdId, 1.5m, null, InvoiceDate, CancellationToken.None)).FxRate.Should().Be(1m);
        (await resolver.ResolveAsync(null, null, null, InvoiceDate, CancellationToken.None)).CurrencyId.Should().Be(UsdId);
    }

    [Fact]
    public async Task Resolve_InactiveCurrency_IsRefusedWithCode()
    {
        using var db = Seed();

        var act = () => new DocumentCurrencyResolver(db).ResolveAsync(GbpId, 1.3m, null, InvoiceDate, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<ValidationException>()).Which;
        ex.Errors.Should().ContainSingle(e => e.ErrorCode == DocumentCurrencyResolver.CurrencyInactiveCode);
    }

    [Fact]
    public async Task CreateInvoice_InheritsTheCustomerCurrency_AndBooksAtTheRateOnFile()
    {
        using var db = Seed();
        var customer = new Customer { Name = "Maple Fabrication", CurrencyId = CadId, IsActive = true };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var result = await NewInvoiceHandler(db).Handle(InvoiceCommand(customer.Id, null, null), CancellationToken.None);

        var invoice = await db.Invoices.SingleAsync(i => i.Id == result.Id);
        invoice.CurrencyId.Should().Be(CadId);
        invoice.FxRate.Should().Be(0.73m);
        result.CurrencyCode.Should().Be("CAD");
        result.TotalFunctional.Should().Be(730m);
    }

    [Fact]
    public async Task CreateInvoice_FromSalesOrder_TakesTheOrderCurrency_AndRefusesAnother()
    {
        using var db = Seed();
        var customer = new Customer { Name = "Maple Fabrication", IsActive = true };
        db.Customers.Add(customer);
        var order = new SalesOrder { OrderNumber = "SO-1", Customer = customer, CurrencyId = CadId, FxRate = 0.72m };
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();

        var result = await NewInvoiceHandler(db).Handle(InvoiceCommand(customer.Id, order.Id, null), CancellationToken.None);
        (await db.Invoices.SingleAsync(i => i.Id == result.Id)).CurrencyId.Should().Be(CadId);

        var act = () => NewInvoiceHandler(db).Handle(InvoiceCommand(customer.Id, order.Id, UsdId), CancellationToken.None);
        var ex = (await act.Should().ThrowAsync<ValidationException>()).Which;
        ex.Errors.Should().ContainSingle(e => e.ErrorCode == DocumentCurrencyResolver.InvoiceSoCurrencyMismatchCode);
    }

    [Fact]
    public async Task CreateInvoice_ForeignWithNoRateOnFile_IsRefused()
    {
        using var db = Seed();
        var customer = new Customer { Name = "Euro Works", CurrencyId = CadId, IsActive = true };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var command = InvoiceCommand(customer.Id, null, null) with
        {
            InvoiceDate = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
        };

        var act = () => NewInvoiceHandler(db).Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<FxRateMissingException>();
        (await db.Invoices.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ConvertQuoteToOrder_CarriesTheQuoteCurrency_WithAFreshRate()
    {
        using var db = Seed();
        var customer = new Customer { Name = "Maple Fabrication", IsActive = true };
        var quote = new Quote
        {
            QuoteNumber = "QT-1", Type = QuoteType.Quote, Customer = customer, Status = QuoteStatus.Accepted, CurrencyId = CadId, FxRate = 0.70m,
            Lines = [new QuoteLine { Description = "Bracket", Quantity = 10, UnitPrice = 100m, LineNumber = 1 }],
        };
        db.Quotes.Add(quote);
        await db.SaveChangesAsync();

        var handler = new ConvertQuoteToOrderHandler(
            new QuoteRepository(db), new SalesOrderRepository(db), new Moq.Mock<IBarcodeService>().Object, db,
            clock: new FixedClock(InvoiceDate));
        var result = await handler.Handle(new ConvertQuoteToOrderCommand(quote.Id), CancellationToken.None);

        var order = await db.SalesOrders.SingleAsync(o => o.Id == result.Id);
        order.CurrencyId.Should().Be(CadId);
        order.FxRate.Should().Be(0.73m);
    }

    [Fact]
    public async Task ApplyToInvoice_RefusesAnInvoiceInAnotherCurrency()
    {
        using var db = Seed();
        var invoice = new Invoice { InvoiceNumber = "INV-1", CustomerId = 5, CurrencyId = UsdId, Status = InvoiceStatus.Sent };
        var payment = new Payment { CustomerId = 5, CurrencyId = CadId, FxRate = 0.75m, Amount = 100m };

        var act = () => PaymentApplicationRules.ApplyToInvoiceAsync(
            db, payment, invoice, new CreatePaymentApplicationModel(1, 50m), new HashSet<int>(), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<ValidationException>()).Which;
        ex.Errors.Should().ContainSingle(e => e.ErrorCode == DocumentCurrencyResolver.PaymentCurrencyMismatchCode);
    }

    [Fact]
    public async Task ApplyToInvoice_OmittedSettlementRate_TakesThePaymentRate()
    {
        using var db = Seed();
        var invoice = new Invoice
        {
            Id = 1, InvoiceNumber = "INV-1", CustomerId = 5, CurrencyId = CadId, FxRate = 0.73m, Status = InvoiceStatus.Sent,
            Lines = [new InvoiceLine { Description = "Bracket", Quantity = 1, UnitPrice = 1000m, LineNumber = 1 }],
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        var payment = new Payment { CustomerId = 5, CurrencyId = CadId, FxRate = 0.75m, Amount = 1000m };

        await PaymentApplicationRules.ApplyToInvoiceAsync(
            db, payment, invoice, new CreatePaymentApplicationModel(1, 1000m), new HashSet<int>(), CancellationToken.None);

        payment.Applications.Single().SettlementFxRate.Should().Be(0.75m);
        invoice.Status.Should().Be(InvoiceStatus.Paid);
    }

    [Fact]
    public async Task PartyCurrency_ChangesUntilTheCustomerHasAnIssuedDocument()
    {
        using var db = Seed();
        var customer = new Customer { Name = "Maple Fabrication", IsActive = true };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        (await PartyCurrency.ApplyToCustomerAsync(db, customer, CadId, null, clear: false, CancellationToken.None)).Should().BeTrue();
        customer.DefaultCurrency.Should().Be("CAD");

        db.Invoices.Add(new Invoice { InvoiceNumber = "INV-1", CustomerId = customer.Id, CurrencyId = CadId, Status = InvoiceStatus.Sent });
        await db.SaveChangesAsync();

        var act = () => PartyCurrency.ApplyToCustomerAsync(db, customer, EurId, null, clear: false, CancellationToken.None);
        var ex = (await act.Should().ThrowAsync<CurrencyLockedException>()).Which;
        ex.Code.Should().Be(CurrencyLockedException.PartyLocked);
    }

    [Fact]
    public async Task PartyCurrency_NamingTheFunctionalCurrencyForALegacyParty_IsNotAChange()
    {
        using var db = Seed();
        var vendor = new Vendor { CompanyName = "Northern Steel", IsActive = true };
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();
        db.PurchaseOrders.Add(new PurchaseOrder { PONumber = "PO-1", VendorId = vendor.Id, Status = PurchaseOrderStatus.Submitted });
        await db.SaveChangesAsync();

        (await PartyCurrency.ApplyToVendorAsync(db, vendor, UsdId, clear: false, CancellationToken.None)).Should().BeFalse();
        vendor.CurrencyId.Should().BeNull();

        vendor.CurrencyId = UsdId;
        (await PartyCurrency.ApplyToVendorAsync(db, vendor, null, clear: true, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task PartyCurrency_VendorWithASubmittedPo_IsLocked()
    {
        using var db = Seed();
        var vendor = new Vendor { CompanyName = "Northern Steel", IsActive = true };
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();
        db.PurchaseOrders.Add(new PurchaseOrder { PONumber = "PO-1", VendorId = vendor.Id, Status = PurchaseOrderStatus.Submitted });
        await db.SaveChangesAsync();

        var act = () => PartyCurrency.ApplyToVendorAsync(db, vendor, CadId, clear: false, CancellationToken.None);

        await act.Should().ThrowAsync<CurrencyLockedException>();
    }

    [Fact]
    public async Task CreateVendorBill_OnAPo_TakesThePoCurrency_AndRefusesAnother()
    {
        using var db = Seed();
        var vendor = new Vendor { CompanyName = "Northern Steel", IsActive = true };
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();
        var line = new PurchaseOrderLine { Description = "Bar stock", OrderedQuantity = 10, ReceivedQuantity = 10, UnitPrice = 10m };
        var po = new PurchaseOrder { PONumber = "PO-1", VendorId = vendor.Id, Status = PurchaseOrderStatus.Received, CurrencyId = CadId, QuoteCurrency = "CAD", FxRate = 0.74m, Lines = [line] };
        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync();
        var handler = new CreateVendorBillHandler(new VendorBillRepository(db), new VendorRepository(db), db);

        var created = await handler.Handle(BillCommand(vendor.Id, po.Id, line.Id, null), CancellationToken.None);

        var bill = await db.VendorBills.SingleAsync(b => b.Id == created.Id);
        bill.CurrencyId.Should().Be(CadId);
        bill.FxRate.Should().Be(0.73m);
        created.CurrencyCode.Should().Be("CAD");

        var act = () => handler.Handle(BillCommand(vendor.Id, po.Id, line.Id, UsdId), CancellationToken.None);
        var ex = (await act.Should().ThrowAsync<ValidationException>()).Which;
        ex.Errors.Should().ContainSingle(e => e.ErrorCode == DocumentCurrencyResolver.BillPoCurrencyMismatchCode);
    }

    [Fact]
    public async Task ReceiptRate_UsesThePoRate_ElseTheRateOnFile_AndOneForFunctional()
    {
        using var db = Seed();
        var currencies = new DocumentCurrencyResolver(db);

        (await PurchaseOrderCurrency.ReceiptRateAsync(currencies, new PurchaseOrder { CurrencyId = CadId, FxRate = 0.74m }, InvoiceDate, CancellationToken.None))
            .Should().Be(0.74m);
        (await PurchaseOrderCurrency.ReceiptRateAsync(currencies, new PurchaseOrder { CurrencyId = CadId }, InvoiceDate, CancellationToken.None))
            .Should().Be(0.73m);
        (await PurchaseOrderCurrency.ReceiptRateAsync(currencies, new PurchaseOrder { QuoteCurrency = "CAD" }, InvoiceDate, CancellationToken.None))
            .Should().Be(0.73m);
        (await PurchaseOrderCurrency.ReceiptRateAsync(currencies, new PurchaseOrder { CurrencyId = UsdId, FxRate = 1.2m }, InvoiceDate, CancellationToken.None))
            .Should().Be(1m);
    }

    [Fact]
    public async Task UpdateCurrency_ChangingTheBase_IsRefusedOnceAJournalEntryExists()
    {
        using var db = Seed();
        var handler = new UpdateCurrencyHandler(db);
        var request = new UpdateCurrencyRequestModel("CAD", "Canadian Dollar", "$", 2, IsBaseCurrency: true, IsActive: true, SortOrder: 0);

        db.Payments.Add(new Payment { PaymentNumber = "PMT-1", CustomerId = 1, Amount = 10m });
        await db.SaveChangesAsync();

        var act = () => handler.Handle(new UpdateCurrencyCommand(CadId, request), CancellationToken.None);
        var ex = (await act.Should().ThrowAsync<CurrencyLockedException>()).Which;
        ex.Code.Should().Be(CurrencyLockedException.BaseLocked);
    }

    [Fact]
    public async Task UpdateCurrency_ChangingTheBase_BeforeAnythingIsBooked_MovesTheBookToo()
    {
        using var db = Seed();

        await new UpdateCurrencyHandler(db).Handle(
            new UpdateCurrencyCommand(CadId, new UpdateCurrencyRequestModel("CAD", "Canadian Dollar", "$", 2, true, true, 0)),
            CancellationToken.None);

        (await db.Books.SingleAsync()).FunctionalCurrencyId.Should().Be(CadId);
        (await db.Currencies.SingleAsync(c => c.Id == UsdId)).IsBaseCurrency.Should().BeFalse();
    }

    [Fact]
    public void BalanceFunctional_PutsTheResidualOnTheLargestNonControlLineOfTheShortSide()
    {
        var lines = new List<JournalLine>
        {
            new() { Debit = 100m, FunctionalAmount = 73.00m },
            new() { Credit = 33.33m, FunctionalAmount = 24.33m },
            new() { Credit = 66.67m, FunctionalAmount = 48.66m },
        };

        ForgeGlPostingEngine.BalanceFunctional(lines, new HashSet<int> { 0 }, 0.01m);

        lines[2].FunctionalAmount.Should().Be(48.67m);
        lines[0].FunctionalAmount.Should().Be(73.00m);
    }

    [Fact]
    public void BalanceFunctional_NeverMovesAControlLine_TakingTheResidualOffTheLongSide()
    {
        var lines = new List<JournalLine>
        {
            new() { Debit = 1m, FunctionalAmount = 0.73m },
            new() { Credit = 0.5m, FunctionalAmount = 0.37m },
            new() { Credit = 0.5m, FunctionalAmount = 0.37m },
        };

        ForgeGlPostingEngine.BalanceFunctional(lines, new HashSet<int> { 0 }, 0.01m);

        lines[0].FunctionalAmount.Should().Be(0.73m);
        (lines[1].FunctionalAmount + lines[2].FunctionalAmount).Should().Be(0.73m);
    }

    [Fact]
    public void BalanceFunctional_ResidualAboveTolerance_IsRefused()
    {
        var lines = new List<JournalLine>
        {
            new() { Debit = 100m, FunctionalAmount = 73.00m },
            new() { Credit = 100m, FunctionalAmount = 72.95m },
        };

        var act = () => ForgeGlPostingEngine.BalanceFunctional(lines, new HashSet<int>(), 0.01m);

        act.Should().Throw<PostingException>().Which.Code.Should().Be("UNBALANCED_FUNCTIONAL");
    }

    [Fact]
    public void FxMath_RateOne_LeavesTheAmountUnrounded()
    {
        FxMath.ToFunctional(10.005m, 1m).Should().Be(10.005m);
        FxMath.ToFunctional(1000m, 0.74m).Should().Be(740m);
        FxMath.ToFunctional(0.5m, 0.73m).Should().Be(0.37m);
    }

    private static CreateInvoiceHandler NewInvoiceHandler(AppDbContext db)
        => new(new InvoiceRepository(db), new CustomerRepository(db), db);

    private static CreateInvoiceCommand InvoiceCommand(int customerId, int? salesOrderId, int? currencyId)
        => new(
            customerId, salesOrderId, null, InvoiceDate, InvoiceDate.AddDays(30), null, 0m, null,
            [new CreateInvoiceLineModel(null, "Machining", 1, 1000m)],
            CurrencyId: currencyId);

    private static CreateVendorBillCommand BillCommand(int vendorId, int poId, int poLineId, int? currencyId)
        => new(
            vendorId, null, poId, InvoiceDate, InvoiceDate.AddDays(30), 0m, null,
            [new CreateVendorBillLineModel(null, "Bar stock", 10, 10m, null, poLineId)],
            CurrencyId: currencyId);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
