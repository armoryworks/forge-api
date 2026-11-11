using System.Text.Json;

using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Customers;
using Forge.Api.Features.PurchaseOrders;
using Forge.Api.Features.SalesOrders;
using Forge.Api.Features.Tolerances;
using Forge.Api.Features.Vendors;
using Forge.Api.Workflows;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Tolerances;

public class ToleranceRaiseGuardTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private static System.Security.Claims.ClaimsPrincipal? User(params string[] roles)
        => TestCaller.As(1, roles).HttpContext!.User;

    private async Task SeedDefaultsAsync(string overReceipt = "5", string overShip = "5")
    {
        _db.SystemSettings.AddRange(
            new SystemSetting { Key = ToleranceSettings.OverReceiptPercentKey, Value = overReceipt },
            new SystemSetting { Key = ToleranceSettings.OverShipPercentKey, Value = overShip });
        await _db.SaveChangesAsync();
    }

    private async Task<Vendor> AddVendorAsync(decimal? over = null)
    {
        var vendor = new Vendor { CompanyName = "Molder", OverReceiptTolerancePct = over };
        _db.Vendors.Add(vendor);
        await _db.SaveChangesAsync();
        return vendor;
    }

    private async Task<Customer> AddCustomerAsync(decimal? over = null)
    {
        var customer = new Customer { Name = "Powersports", OverShipTolerancePct = over };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        return customer;
    }

    private async Task<(PurchaseOrder Po, PurchaseOrderLine Line)> AddDraftPurchaseOrderAsync(decimal? vendorOver, decimal? lineOver)
    {
        var vendor = await AddVendorAsync(vendorOver);
        var line = new PurchaseOrderLine { Description = "Resin", OrderedQuantity = 100m, UnitPrice = 2m, OverReceiptTolerancePct = lineOver };
        var po = new PurchaseOrder { PONumber = "PO-RAISE-1", VendorId = vendor.Id, Status = PurchaseOrderStatus.Draft, Lines = [line] };
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();
        return (po, line);
    }

    private async Task<(SalesOrder Order, SalesOrderLine Line)> AddDraftSalesOrderAsync(decimal? customerOver, decimal? lineOver)
    {
        var customer = await AddCustomerAsync(customerOver);
        var line = new SalesOrderLine { Description = "Bracket", Quantity = 100m, UnitPrice = 5m, LineNumber = 1, OverShipTolerancePct = lineOver };
        var order = new SalesOrder { OrderNumber = "SO-RAISE-1", CustomerId = customer.Id, Status = SalesOrderStatus.Draft, Lines = [line] };
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();
        return (order, line);
    }

    private static async Task ShouldRefuseAsync(Func<Task> act, string property, decimal current)
    {
        var failure = (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Single();
        failure.ErrorCode.Should().Be(ToleranceRaiseGuard.Code);
        failure.PropertyName.Should().Be(property);
        failure.FormattedMessagePlaceholderValues["ComparisonValue"].Should().Be(current);
    }

    [Theory]
    [InlineData("OfficeManager")]
    [InlineData("Procurement")]
    [InlineData("PM")]
    [InlineData("Engineer")]
    public async Task ARoleThatCannotOverride_CannotRaiseAVendorTolerance(string role)
    {
        await SeedDefaultsAsync();

        var act = () => ToleranceRaiseGuard.EnsureVendorAsync(_db, 5m, 10m, "OverReceiptTolerancePct", User(role), CancellationToken.None);

        await ShouldRefuseAsync(act, "OverReceiptTolerancePct", 5m);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    public async Task AnOverrideRole_RaisesAVendorTolerance(string role)
    {
        await SeedDefaultsAsync();

        var act = () => ToleranceRaiseGuard.EnsureVendorAsync(_db, 5m, 10m, "OverReceiptTolerancePct", User(role), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(10.0, 4.0)]
    [InlineData(10.0, 10.0)]
    [InlineData(10.0, 0.0)]
    public async Task LoweringOrKeepingATolerance_IsOpenToTheEditingRoles(double before, double after)
    {
        await SeedDefaultsAsync();

        var act = () => ToleranceRaiseGuard.EnsureVendorAsync(
            _db, (decimal)before, (decimal)after, "OverReceiptTolerancePct", User("Procurement"), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ABlankVendorValue_IsMeasuredAgainstTheCompanyDefault()
    {
        await SeedDefaultsAsync(overReceipt: "5");

        var same = () => ToleranceRaiseGuard.EnsureVendorAsync(_db, null, 5m, "p", User("Procurement"), CancellationToken.None);
        var above = () => ToleranceRaiseGuard.EnsureVendorAsync(_db, null, 5.01m, "p", User("Procurement"), CancellationToken.None);

        await same.Should().NotThrowAsync();
        await ShouldRefuseAsync(above, "p", 5m);
    }

    [Fact]
    public async Task ClearingAValueBelowTheCompanyDefault_IsARaise()
    {
        await SeedDefaultsAsync(overReceipt: "5");

        var act = () => ToleranceRaiseGuard.EnsureVendorAsync(_db, 2m, null, "p", User("OfficeManager"), CancellationToken.None);

        await ShouldRefuseAsync(act, "p", 2m);
    }

    [Fact]
    public async Task ACallWithNoSignedInUser_CannotRaise()
    {
        var act = () => ToleranceRaiseGuard.EnsureCustomerAsync(_db, null, 3m, "p", null, CancellationToken.None);

        await ShouldRefuseAsync(act, "p", 0m);
    }

    [Fact]
    public async Task APurchaseLine_IsMeasuredAgainstItsOwnValueThenItsVendor()
    {
        await SeedDefaultsAsync(overReceipt: "5");
        var vendor = await AddVendorAsync(over: 10m);

        Task Check(decimal? before, decimal? after) => ToleranceRaiseGuard.EnsurePurchaseLinesAsync(
            _db, vendor.Id, [("p", before, after)], User("Procurement"), CancellationToken.None);

        await FluentActions.Invoking(() => Check(null, 10m)).Should().NotThrowAsync();
        await FluentActions.Invoking(() => Check(null, null)).Should().NotThrowAsync();
        await FluentActions.Invoking(() => Check(20m, 15m)).Should().NotThrowAsync();
        await ShouldRefuseAsync(() => Check(null, 12m), "p", 10m);
        await ShouldRefuseAsync(() => Check(20m, 21m), "p", 20m);
    }

    [Fact]
    public async Task ASalesLine_IsMeasuredAgainstItsOwnValueThenItsCustomerThenTheDefault()
    {
        await SeedDefaultsAsync(overShip: "5");
        var plain = await AddCustomerAsync();
        var generous = await AddCustomerAsync(over: 8m);

        Task Check(int customerId, decimal? before, decimal? after) => ToleranceRaiseGuard.EnsureSalesLinesAsync(
            _db, customerId, [("p", before, after)], User("PM"), CancellationToken.None);

        await FluentActions.Invoking(() => Check(plain.Id, null, 5m)).Should().NotThrowAsync();
        await FluentActions.Invoking(() => Check(generous.Id, null, 8m)).Should().NotThrowAsync();
        await ShouldRefuseAsync(() => Check(plain.Id, null, 6m), "p", 5m);
        await ShouldRefuseAsync(() => Check(generous.Id, null, 9m), "p", 8m);
        await FluentActions.Invoking(() => ToleranceRaiseGuard.EnsureSalesLinesAsync(
            _db, plain.Id, [("p", null, 50m)], User("Manager"), CancellationToken.None)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateVendor_RefusesARaiseBeforeAnythingIsSaved()
    {
        var repo = new Mock<IVendorRepository>();
        var handler = new CreateVendorHandler(
            repo.Object, Mock.Of<ISystemSettingRepository>(), Mock.Of<IBusinessIdentifierService>(), _db, TestCaller.As(1, "Procurement"));

        var act = () => handler.Handle(
            new CreateVendorCommand("Molder", null, null, null, null, null, null, null, null, null, null, OverReceiptTolerancePct: 5m),
            CancellationToken.None);

        await ShouldRefuseAsync(act, "OverReceiptTolerancePct", 0m);
        repo.Verify(r => r.AddAsync(It.IsAny<Vendor>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private UpdateVendorHandler UpdateVendorHandler(Vendor vendor, IHttpContextAccessor caller)
    {
        var repo = new Mock<IVendorRepository>();
        repo.Setup(r => r.FindAsync(vendor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(vendor);
        return new UpdateVendorHandler(
            repo.Object, Mock.Of<ISystemSettingRepository>(), Mock.Of<IBusinessIdentifierService>(),
            _db, Mock.Of<IClock>(), StubCapabilitySnapshotProvider.Off, caller);
    }

    private static UpdateVendorCommand VendorTolerance(int id, decimal? over, bool clear = false)
        => new(id, null, null, null, null, null, null, null, null, null, null, null, null, null,
            OverReceiptTolerancePct: over, ClearOverReceiptTolerance: clear ? true : null);

    [Fact]
    public async Task UpdateVendor_RefusesARaiseFromTheOfficeManagerAndChangesNothing()
    {
        var vendor = await AddVendorAsync(over: 5m);

        var act = () => UpdateVendorHandler(vendor, TestCaller.As(1, "OfficeManager"))
            .Handle(VendorTolerance(vendor.Id, 25m), CancellationToken.None);

        await ShouldRefuseAsync(act, "OverReceiptTolerancePct", 5m);
        vendor.OverReceiptTolerancePct.Should().Be(5m);
    }

    [Fact]
    public async Task UpdateVendor_LetsTheOfficeManagerLowerItAndAManagerRaiseIt()
    {
        var vendor = await AddVendorAsync(over: 5m);

        await UpdateVendorHandler(vendor, TestCaller.As(1, "OfficeManager")).Handle(VendorTolerance(vendor.Id, 2m), CancellationToken.None);
        vendor.OverReceiptTolerancePct.Should().Be(2m);

        await UpdateVendorHandler(vendor, TestCaller.As(2, "Manager")).Handle(VendorTolerance(vendor.Id, 25m), CancellationToken.None);
        vendor.OverReceiptTolerancePct.Should().Be(25m);
    }

    private VendorWorkflowAdapter Wizard(params string[] roles)
        => new(_db, Mock.Of<IVendorRepository>(), Mock.Of<IBusinessIdentifierService>(), Mock.Of<ISystemSettingRepository>(),
            TestCaller.As(1, roles));

    [Fact]
    public async Task TheVendorWizard_RefusesARaiseOnCreateAndOnALaterStep()
    {
        var vendor = await AddVendorAsync(over: 5m);
        var fields = JsonSerializer.SerializeToElement(
            new Dictionary<string, object> { ["companyName"] = "Molder", ["overReceiptTolerancePct"] = 12 });

        await ShouldRefuseAsync(() => Wizard("Procurement").CreateDraftAsync(fields, CancellationToken.None), "overReceiptTolerancePct", 0m);
        await ShouldRefuseAsync(() => Wizard("Procurement").ApplyAsync(vendor.Id, fields, CancellationToken.None), "overReceiptTolerancePct", 5m);
        (await _db.Vendors.AsNoTracking().SingleAsync()).OverReceiptTolerancePct.Should().Be(5m);

        await Wizard("Manager").ApplyAsync(vendor.Id, fields, CancellationToken.None);
        (await _db.Vendors.AsNoTracking().SingleAsync()).OverReceiptTolerancePct.Should().Be(12m);
    }

    [Fact]
    public async Task UpdateCustomer_RefusesARaiseFromTheOfficeManagerAndTakesOneFromAManager()
    {
        var customer = await AddCustomerAsync(over: 3m);
        UpdateCustomerHandler Handler(string role) => new(
            new CustomerRepository(_db), Mock.Of<ISystemSettingRepository>(), Mock.Of<IBusinessIdentifierService>(),
            _db, Mock.Of<IClock>(), TestCaller.As(1, role));
        var raise = new UpdateCustomerCommand(customer.Id, null, null, null, null, null, OverShipTolerancePct: 10m);

        await ShouldRefuseAsync(() => Handler("OfficeManager").Handle(raise, CancellationToken.None), "OverShipTolerancePct", 3m);
        (await _db.Customers.AsNoTracking().SingleAsync()).OverShipTolerancePct.Should().Be(3m);

        await Handler("Manager").Handle(raise, CancellationToken.None);
        (await _db.Customers.AsNoTracking().SingleAsync()).OverShipTolerancePct.Should().Be(10m);
    }

    [Fact]
    public async Task CreatePurchaseOrder_RefusesALineAboveTheVendorLevel()
    {
        var vendor = await AddVendorAsync(over: 5m);
        var vendors = new Mock<IVendorRepository>();
        vendors.Setup(r => r.FindAsync(vendor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(vendor);
        var orders = new Mock<IPurchaseOrderRepository>();
        var handler = new CreatePurchaseOrderHandler(
            orders.Object, vendors.Object, Mock.Of<IPartRepository>(), Mock.Of<IBarcodeService>(),
            Mock.Of<ISystemSettingRepository>(), Mock.Of<IBusinessIdentifierService>(), Mock.Of<MediatR.IMediator>(),
            TestCaller.As(1, "Procurement"), _db, Mock.Of<IClock>());

        var act = () => handler.Handle(new CreatePurchaseOrderCommand(vendor.Id, null, null,
        [
            new CreatePurchaseOrderLineModel(null, "Resin", 100m, 2m, null),
            new CreatePurchaseOrderLineModel(null, "Colorant", 10m, 9m, null, OverReceiptTolerancePct: 50m),
        ]), CancellationToken.None);

        await ShouldRefuseAsync(act, "lines[1].overReceiptTolerancePct", 5m);
        orders.Verify(r => r.AddAsync(It.IsAny<PurchaseOrder>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddPurchaseOrderLine_RefusesALineAboveTheVendorLevel()
    {
        var (po, _) = await AddDraftPurchaseOrderAsync(vendorOver: 5m, lineOver: 5m);
        var handler = new AddPurchaseOrderLineHandler(
            new PurchaseOrderRepository(_db), Mock.Of<IPartRepository>(), _db, Mock.Of<MediatR.IMediator>(), TestCaller.As(1, "OfficeManager"));

        var act = () => handler.Handle(new AddPurchaseOrderLineCommand(po.Id,
            new AddPurchaseOrderLineRequestModel(null, "Colorant", 10m, 9m, null, OverReceiptTolerancePct: 6m)), CancellationToken.None);

        await ShouldRefuseAsync(act, "OverReceiptTolerancePct", 5m);
        (await _db.PurchaseOrderLines.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UpdatePurchaseOrderLine_RefusesARaiseAndTakesALowerValueOrAManager()
    {
        var (po, line) = await AddDraftPurchaseOrderAsync(vendorOver: 5m, lineOver: 5m);
        UpdatePurchaseOrderLineHandler Handler(string role) => new(
            new PurchaseOrderRepository(_db), Mock.Of<MediatR.IMediator>(), _db, TestCaller.As(1, role));
        UpdatePurchaseOrderLineCommand Edit(decimal over) => new(po.Id, line.Id,
            new UpdateOrderLineRequestModel("Resin", 100m, 2m, null, OverTolerancePct: over));

        await ShouldRefuseAsync(() => Handler("Procurement").Handle(Edit(8m), CancellationToken.None), "OverTolerancePct", 5m);
        (await _db.PurchaseOrderLines.AsNoTracking().SingleAsync()).OverReceiptTolerancePct.Should().Be(5m);

        await Handler("Procurement").Handle(Edit(3m), CancellationToken.None);
        (await _db.PurchaseOrderLines.AsNoTracking().SingleAsync()).OverReceiptTolerancePct.Should().Be(3m);

        await Handler("Manager").Handle(Edit(8m), CancellationToken.None);
        (await _db.PurchaseOrderLines.AsNoTracking().SingleAsync()).OverReceiptTolerancePct.Should().Be(8m);
    }

    [Fact]
    public async Task CreateSalesOrder_RefusesALineAboveTheCustomerLevel()
    {
        var customer = await AddCustomerAsync(over: 5m);
        var customers = new Mock<ICustomerRepository>();
        customers.Setup(r => r.FindAsync(customer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(customer);
        var orders = new Mock<ISalesOrderRepository>();
        var handler = new CreateSalesOrderHandler(
            orders.Object, customers.Object, Mock.Of<IPartRepository>(), Mock.Of<IBarcodeService>(),
            Mock.Of<ICustomerAddressRepository>(), db: _db, httpContext: TestCaller.As(1, "OfficeManager"));

        var act = () => handler.Handle(new CreateSalesOrderCommand(customer.Id, null, null, null, null, null, null, null, 0m,
            [new CreateSalesOrderLineModel(null, "Bracket", 100m, 5m, null, OverShipTolerancePct: 20m)]), CancellationToken.None);

        await ShouldRefuseAsync(act, "lines[0].overShipTolerancePct", 5m);
        orders.Verify(r => r.AddAsync(It.IsAny<SalesOrder>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddSalesOrderLine_RefusesALineAboveTheCustomerLevel()
    {
        var (order, _) = await AddDraftSalesOrderAsync(customerOver: null, lineOver: null);
        var handler = new AddSalesOrderLineHandler(
            new SalesOrderRepository(_db), Mock.Of<IPartRepository>(), _db, Mock.Of<MediatR.IMediator>(),
            httpContext: TestCaller.As(1, "PM"));

        var act = () => handler.Handle(new AddSalesOrderLineCommand(order.Id,
            new CreateSalesOrderLineModel(null, "Bracket", 10m, 5m, null, OverShipTolerancePct: 1m)), CancellationToken.None);

        await ShouldRefuseAsync(act, "OverShipTolerancePct", 0m);
        (await _db.SalesOrderLines.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UpdateSalesOrderLine_RefusesARaiseAndTakesALowerValueOrAnAdmin()
    {
        var (order, line) = await AddDraftSalesOrderAsync(customerOver: 10m, lineOver: null);
        UpdateSalesOrderLineHandler Handler(string role) => new(
            new SalesOrderRepository(_db), Mock.Of<MediatR.IMediator>(), _db, TestCaller.As(1, role));
        UpdateSalesOrderLineCommand Edit(decimal over) => new(order.Id, line.Id,
            new UpdateOrderLineRequestModel("Bracket", 100m, 5m, null, OverTolerancePct: over));

        await ShouldRefuseAsync(() => Handler("OfficeManager").Handle(Edit(11m), CancellationToken.None), "OverTolerancePct", 10m);
        (await _db.SalesOrderLines.AsNoTracking().SingleAsync()).OverShipTolerancePct.Should().BeNull();

        await Handler("OfficeManager").Handle(Edit(4m), CancellationToken.None);
        (await _db.SalesOrderLines.AsNoTracking().SingleAsync()).OverShipTolerancePct.Should().Be(4m);

        await Handler("Admin").Handle(Edit(30m), CancellationToken.None);
        (await _db.SalesOrderLines.AsNoTracking().SingleAsync()).OverShipTolerancePct.Should().Be(30m);
    }
}
