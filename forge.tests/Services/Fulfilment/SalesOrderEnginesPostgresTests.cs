using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.Cpq;
using Forge.Api.Features.Invoices;
using Forge.Api.Features.PurchaseOrders;
using Forge.Api.Features.Quotes;
using Forge.Api.Features.SalesTax;
using Forge.Api.Services;
using Forge.Api.Services.Cpq;
using Forge.Api.Services.Fulfilment;
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

namespace Forge.Tests.Services.Fulfilment;

/// <summary>
/// The sales order engines against real Postgres: drop-ship and back-to-back purchase orders raised through the
/// normal PO create path and linked to the sales order line, the drop-ship delivery that ships the line without
/// moving stock (and posts its cost to COGS), the back-to-back receipt reserved to its line, and the configured
/// quote in the customer's currency.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SalesOrderEnginesPostgresTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const int UsdId = 1;
    private const int EurId = 2;
    private const int BookId = 1;
    private const int CogsId = 500;
    private const int GrniId = 210;
    private const int InvRawId = 130;

    private static readonly IClock Clock = new SystemClock();
    private static readonly IHttpContextAccessor NoUser = new HttpContextAccessor();

    private sealed record Seed(int CustomerId, int AddressId, int PartId, int VendorId, int OrderId, int LineId);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
    }

    private static Task ResetAsync(AppDbContext db)
        => db.Database.ExecuteSqlRawAsync(@"
DO $$
DECLARE r RECORD;
BEGIN
  FOR r IN (SELECT tablename FROM pg_tables
            WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory') LOOP
    EXECUTE 'TRUNCATE TABLE ' || quote_ident(r.tablename) || ' RESTART IDENTITY CASCADE';
  END LOOP;
END $$;");

    private static async Task<Seed> SeedOrderAsync(AppDbContext db, decimal quantity = 5m, int? customerCurrencyId = null)
    {
        db.Set<Currency>().AddRange(
            new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$", IsBaseCurrency = true },
            new Currency { Id = EurId, Code = "EUR", Name = "Euro", Symbol = "€" });
        var customer = new Customer { Name = "Engines Customer", CurrencyId = customerCurrencyId };
        var vendor = new Vendor { CompanyName = "Engines Vendor" };
        var part = new Part { PartNumber = "WID-1", Name = "Widget", Description = "Widget", InventoryClass = InventoryClass.Raw, ProcurementSource = ProcurementSource.Buy };
        db.AddRange(customer, vendor, part);
        await db.SaveChangesAsync();

        var address = new CustomerAddress
        {
            CustomerId = customer.Id, Label = "Dock", AddressType = AddressType.Shipping,
            Line1 = "1 Main St", City = "Springfield", State = "UT", PostalCode = "84000", IsDefault = true,
        };
        db.Add(address);
        await db.SaveChangesAsync();

        var order = new SalesOrder
        {
            OrderNumber = "SO-00001", CustomerId = customer.Id, Status = SalesOrderStatus.Confirmed, ShippingAddressId = address.Id,
        };
        order.Lines.Add(new SalesOrderLine { PartId = part.Id, Description = "Widget", Quantity = quantity, UnitPrice = 40m, LineNumber = 1 });
        db.Add(order);
        await db.SaveChangesAsync();
        return new Seed(customer.Id, address.Id, part.Id, vendor.Id, order.Id, order.Lines.Single().Id);
    }

    private static async Task SeedLedgerAsync(AppDbContext db)
    {
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        var year = DateTime.UtcNow.Year;
        db.Set<FiscalYear>().Add(new FiscalYear
        {
            Id = 10, BookId = BookId, Name = $"FY{year}",
            StartDate = new DateOnly(year, 1, 1), EndDate = new DateOnly(year, 12, 31), Status = FiscalYearStatus.Open,
        });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = 1000, FiscalYearId = 10, PeriodNumber = 1, Name = $"FY{year}",
            StartDate = new DateOnly(year, 1, 1), EndDate = new DateOnly(year, 12, 31), Status = FiscalPeriodStatus.Open,
        });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = InvRawId, BookId = BookId, AccountNumber = "13100", Name = "Inventory — Raw", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.Inventory, IsPostable = true, IsActive = true },
            new GlAccount { Id = GrniId, BookId = BookId, AccountNumber = "21000", Name = "GRNI", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = CogsId, BookId = BookId, AccountNumber = "50000", Name = "COGS", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        foreach (var (key, accountId) in new[] { ("INVENTORY_RAW", InvRawId), ("GRNI", GrniId), ("COGS", CogsId) })
            db.Set<AccountDeterminationRule>().Add(new AccountDeterminationRule { BookId = BookId, Key = key, GlAccountId = accountId });
        await db.SaveChangesAsync();
    }

    private static Mock<IMediator> Mediator(AppDbContext db, bool fullGl = false)
    {
        var mediator = new Mock<IMediator>();
        var capabilities = new StubCapabilitySnapshotProvider(fullGl ? ["CAP-ACCT-FULLGL"] : []);
        var posting = new ReceiptInventoryPostingService(
            db, new ForgeGlPostingEngine(db, new AccountDeterminationResolver(db), new AcctNumberSequenceAllocator(db), Clock), capabilities);
        mediator
            .Setup(m => m.Send(It.IsAny<CreatePurchaseOrderCommand>(), It.IsAny<CancellationToken>()))
            .Returns((CreatePurchaseOrderCommand c, CancellationToken ct) => new CreatePurchaseOrderHandler(
                new PurchaseOrderRepository(db), new VendorRepository(db), new PartRepository(db, new PartPricingResolver(db)),
                new BarcodeService(db, NoUser), new SystemSettingRepository(db), new BusinessIdentifierService(db, Clock),
                mediator.Object, NoUser, db, Clock).Handle(c, ct));
        mediator
            .Setup(m => m.Send(It.IsAny<ReceiveItemsCommand>(), It.IsAny<CancellationToken>()))
            .Returns((ReceiveItemsCommand c, CancellationToken ct) => new ReceiveItemsHandler(
                new PurchaseOrderRepository(db), Clock, mediator.Object, NoUser, db, posting,
                new InventoryRepository(db), capabilities).Handle(c, ct));
        mediator
            .Setup(m => m.Send(It.IsAny<CreateQuoteCommand>(), It.IsAny<CancellationToken>()))
            .Returns((CreateQuoteCommand c, CancellationToken ct) => new CreateQuoteHandler(
                new QuoteRepository(db), new CustomerRepository(db), new PartRepository(db, new PartPricingResolver(db)),
                systemSettings: new SystemSettingRepository(db), identifiers: new BusinessIdentifierService(db, Clock),
                httpContext: NoUser, db: db, clock: Clock).Handle(c, ct));
        mediator
            .Setup(m => m.Send(It.IsAny<GetTaxRateForCustomerQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SalesTaxRateResponseModel?)null);
        return mediator;
    }

    private static BuiltInDropShipService DropShip(AppDbContext db, Mock<IMediator> mediator)
        => new(db, new LinkedPurchaseOrders(db, mediator.Object, Clock), new ShipmentRepository(db),
            new BusinessIdentifierService(db, Clock), mediator.Object, Clock);

    private static BuiltInBackToBackService BackToBack(AppDbContext db, Mock<IMediator> mediator)
        => new(db, new LinkedPurchaseOrders(db, mediator.Object, Clock));

    private static async Task SubmitAsync(AppDbContext db, int poId)
    {
        var po = await db.PurchaseOrders.SingleAsync(p => p.Id == poId);
        po.Status = PurchaseOrderStatus.Submitted;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_drop_ship_raises_one_po_linked_to_the_line_and_shipping_to_the_customer()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        var seed = await SeedOrderAsync(db);

        var po = await DropShip(db, Mediator(db)).CreateDropShipPurchaseOrderAsync(
            seed.LineId, new CreateDropShipRequestModel { VendorId = seed.VendorId, UnitCost = 12m }, CancellationToken.None);

        await using var verify = fixture.CreateContext();
        var saved = await verify.PurchaseOrders.Include(p => p.Lines).SingleAsync();
        saved.Id.Should().Be(po.Id);
        saved.FulfilmentType.Should().Be(FulfilmentType.DropShip);
        saved.DropShipAddressId.Should().Be(seed.AddressId);
        saved.Status.Should().Be(PurchaseOrderStatus.Draft);
        saved.PONumber.Should().NotBeNullOrEmpty();
        var line = saved.Lines.Single();
        (line.SalesOrderLineId, line.OrderedQuantity, line.UnitPrice).Should().Be((seed.LineId, 5m, 12m));
    }

    [Fact]
    public async Task A_line_cannot_be_bought_twice()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        var seed = await SeedOrderAsync(db);
        var mediator = Mediator(db);
        await DropShip(db, mediator).CreateDropShipPurchaseOrderAsync(
            seed.LineId, new CreateDropShipRequestModel { VendorId = seed.VendorId, Quantity = 3m }, CancellationToken.None);

        var over = () => BackToBack(db, mediator).CreateBackToBackOrderAsync(
            seed.LineId, new CreateBackToBackRequestModel { VendorId = seed.VendorId, Quantity = 3m }, CancellationToken.None);

        await over.Should().ThrowAsync<InvalidOperationException>().WithMessage("*needs only 2*");
        (await db.PurchaseOrders.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Confirming_a_drop_ship_ships_the_line_without_moving_stock_and_posts_cogs()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        var seed = await SeedOrderAsync(db);
        await SeedLedgerAsync(db);
        var mediator = Mediator(db, fullGl: true);
        var service = DropShip(db, mediator);
        var po = await service.CreateDropShipPurchaseOrderAsync(
            seed.LineId, new CreateDropShipRequestModel { VendorId = seed.VendorId, UnitCost = 12m }, CancellationToken.None);
        await SubmitAsync(db, po.Id);
        var poLineId = (await db.PurchaseOrderLines.SingleAsync()).Id;

        (await service.GetPendingDropShipsAsync(CancellationToken.None)).Should().ContainSingle(p => p.PurchaseOrderLineId == poLineId);

        await service.ConfirmDropShipDeliveryAsync(poLineId, 5m, "1Z999", CancellationToken.None);

        await using var verify = fixture.CreateContext();
        var line = await verify.SalesOrderLines.SingleAsync();
        line.ShippedQuantity.Should().Be(5m);
        (await verify.SalesOrders.SingleAsync()).Status.Should().Be(SalesOrderStatus.Shipped);
        var shipment = await verify.Shipments.Include(s => s.Lines).SingleAsync();
        (shipment.FulfilmentType, shipment.Status, shipment.TrackingNumber).Should().Be((FulfilmentType.DropShip, ShipmentStatus.Delivered, "1Z999"));
        shipment.Lines.Single().InventoryRelievedAt.Should().NotBeNull();
        (await verify.BinContents.AnyAsync()).Should().BeFalse();
        (await verify.BinMovements.AnyAsync()).Should().BeFalse();
        (await verify.PurchaseOrderLines.SingleAsync()).ReceivedQuantity.Should().Be(5m);
        var entry = await verify.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == CogsId).Debit.Should().Be(60m);
        entry.Lines.Single(l => l.GlAccountId == GrniId).Credit.Should().Be(60m);
        entry.Lines.Should().NotContain(l => l.GlAccountId == InvRawId);
        (await service.GetPendingDropShipsAsync(CancellationToken.None)).Should().BeEmpty();

        var invoice = await new CreateInvoiceHandler(new InvoiceRepository(verify), new CustomerRepository(verify), verify).Handle(
            new CreateInvoiceCommand(
                seed.CustomerId, seed.OrderId, shipment.Id, Clock.UtcNow, Clock.UtcNow.AddDays(30), null, 0m, null,
                [new CreateInvoiceLineModel(seed.PartId, "Widget", 5m, 40m, SalesOrderLineId: seed.LineId)]),
            CancellationToken.None);
        invoice.Id.Should().BePositive("the delivered drop-ship shipment is invoiceable");
    }

    [Fact]
    public async Task A_drop_ship_po_cannot_be_received_into_stock()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        var seed = await SeedOrderAsync(db);
        var mediator = Mediator(db);
        var po = await DropShip(db, mediator).CreateDropShipPurchaseOrderAsync(
            seed.LineId, new CreateDropShipRequestModel { VendorId = seed.VendorId }, CancellationToken.None);
        await SubmitAsync(db, po.Id);
        var poLineId = (await db.PurchaseOrderLines.SingleAsync()).Id;

        var receive = () => mediator.Object.Send(
            new ReceiveItemsCommand(po.Id, [new ReceiveLineModel(poLineId, 5m, null, null)]), CancellationToken.None);

        await receive.Should().ThrowAsync<InvalidOperationException>().WithMessage("*drop-ship*Confirm the delivery*");
    }

    [Fact]
    public async Task A_back_to_back_receipt_is_reserved_to_its_sales_order_line()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        var seed = await SeedOrderAsync(db, quantity: 4m);
        db.StorageLocations.Add(new StorageLocation { Name = "A1", LocationType = LocationType.Bin, IsActive = true, IsDefault = true });
        await db.SaveChangesAsync();
        var mediator = Mediator(db);
        var service = BackToBack(db, mediator);
        var po = await service.CreateBackToBackOrderAsync(
            seed.LineId, new CreateBackToBackRequestModel { VendorId = seed.VendorId, UnitCost = 9m }, CancellationToken.None);
        po.FulfilmentType.Should().Be(FulfilmentType.BackToBack);
        po.DropShipAddressId.Should().BeNull();
        await SubmitAsync(db, po.Id);
        var poLineId = (await db.PurchaseOrderLines.SingleAsync()).Id;

        await mediator.Object.Send(new ReceiveItemsCommand(po.Id, [new ReceiveLineModel(poLineId, 4m, null, null)]), CancellationToken.None);

        await using var verify = fixture.CreateContext();
        var reservation = await verify.Reservations.SingleAsync();
        (reservation.SalesOrderLineId, reservation.Quantity, reservation.PartId).Should().Be((seed.LineId, 4m, seed.PartId));
        (await verify.BinContents.SingleAsync()).ReservedQuantity.Should().Be(4m);
        var pending = await BackToBack(verify, Mediator(verify)).GetPendingBackToBacksAsync(CancellationToken.None);
        pending.Should().ContainSingle().Which.Should().Match<BackToBackStatusResponseModel>(p =>
            p.Status == "ReadyToShip" && p.ReservedQuantity == 4m && p.ReceivedQuantity == 4m);

        var record = await verify.ReceivingRecords.SingleAsync();
        await BackToBack(verify, Mediator(verify)).LinkReceiptToSalesOrderAsync(poLineId, record.Id, CancellationToken.None);
        (await fixture.CreateContext().Reservations.CountAsync()).Should().Be(1, "linking again tops up only what the line still needs");
    }

    [Fact]
    public async Task A_configured_quote_is_a_draft_in_the_customers_currency()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        var seed = await SeedOrderAsync(db, customerCurrencyId: EurId);
        db.Set<ExchangeRate>().Add(new ExchangeRate
        {
            FromCurrencyId = EurId, ToCurrencyId = UsdId, Rate = 1.25m,
            EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), Source = ExchangeRateSource.Manual,
        });
        var configurator = new ProductConfigurator { Name = "Widget builder", BasePartId = seed.PartId, BasePrice = 100m };
        configurator.Options.Add(new ConfiguratorOption
        {
            Name = "Size", OptionType = ConfiguratorOptionType.Select, IsRequired = true, ValuesJson = """["S", "L"]""",
            PricingRuleJson = """[{"when": "L", "type": "PerUnit", "amount": 25}]""",
        });
        db.Add(configurator);
        await db.SaveChangesAsync();

        var mediator = Mediator(db);
        var cpq = new BuiltInCpqService(db, mediator.Object, Clock, new SystemSettingRepository(db),
            Mock.Of<IBomRevisionService>(), new TaxOverrideGuard(db, mediator.Object, Clock));
        var saved = await new SaveConfigurationHandler(db, cpq, Clock).Handle(
            new SaveConfigurationCommand(new SaveConfigurationRequestModel(configurator.Id, new() { ["Size"] = "L" }, 4m)),
            CancellationToken.None);
        saved.ComputedPrice.Should().Be(125m);

        var quoteId = await new GenerateQuoteFromConfigHandler(db, cpq).Handle(
            new GenerateQuoteFromConfigCommand(saved.Id, new GenerateQuoteFromConfigRequestModel(seed.CustomerId)), CancellationToken.None);

        await using var verify = fixture.CreateContext();
        var quote = await verify.Quotes.Include(q => q.Lines).SingleAsync(q => q.Id == quoteId);
        quote.Status.Should().Be(QuoteStatus.Draft);
        (quote.CurrencyId, quote.FxRate).Should().Be((EurId, 1.25m));
        quote.ExpirationDate!.Value.Date.Should().Be(DateTime.UtcNow.Date.AddDays(BuiltInCpqService.DefaultQuoteValidDays));
        var line = quote.Lines.Single();
        (line.Quantity, line.UnitPrice).Should().Be((4m, 100m));
        line.Notes.Should().Be("Size: L");
        var configuration = await verify.ProductConfigurations.SingleAsync();
        (configuration.QuoteId, configuration.Status).Should().Be((quoteId, ConfigurationStatus.Quoted));
    }
}
