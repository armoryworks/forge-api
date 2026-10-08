using FluentAssertions;
using FluentValidation;
using Moq;
using QuestPDF.Infrastructure;

using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.PurchaseOrders;

public class PurchaseOrderVendorContactTests
{
    static PurchaseOrderVendorContactTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private readonly Mock<IPurchaseOrderRepository> _poRepo = new();
    private readonly Mock<IVendorRepository> _vendorRepo = new();
    private readonly Mock<IPartRepository> _partRepo = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private PurchaseOrder? _added;

    public PurchaseOrderVendorContactTests()
    {
        _poRepo.Setup(r => r.GenerateNextPONumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("PO-00100");
        _poRepo.Setup(r => r.AddAsync(It.IsAny<PurchaseOrder>(), It.IsAny<CancellationToken>()))
            .Callback<PurchaseOrder, CancellationToken>((po, _) => _added = po)
            .Returns(Task.CompletedTask);
        _poRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    private CreatePurchaseOrderHandler CreateHandler() => new(
        _poRepo.Object, _vendorRepo.Object, _partRepo.Object,
        Mock.Of<IBarcodeService>(),
        Mock.Of<ISystemSettingRepository>(),
        Mock.Of<IBusinessIdentifierService>(),
        Mock.Of<MediatR.IMediator>(),
        Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
        _db,
        Mock.Of<IClock>());

    private UpdatePurchaseOrderHandler UpdateHandler() => new(
        _poRepo.Object, Mock.Of<ISystemSettingRepository>(), Mock.Of<IBusinessIdentifierService>(), _db);

    private async Task SeedAsync()
    {
        var vendor = new Vendor { Id = 1, CompanyName = "Midwest Steel Supply", Fax = "555-0109", ContactName = "Front Desk" };
        _db.Vendors.Add(vendor);
        _db.Vendors.Add(new Vendor { Id = 2, CompanyName = "Other Supply" });
        _vendorRepo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(vendor);

        _db.VendorContacts.AddRange(
            new VendorContact { Id = 10, VendorId = 1, FirstName = "Sam", LastName = "Sales" },
            new VendorContact { Id = 11, VendorId = 1, FirstName = "Old", LastName = "Primary", IsPrimary = true, IsActive = false },
            new VendorContact { Id = 12, VendorId = 1, FirstName = "Jane", LastName = "Doe", IsPrimary = true, Email = "jane@example.test", Phone = "555-0110", Fax = "555-0111" },
            new VendorContact { Id = 20, VendorId = 2, FirstName = "Other", LastName = "Person", IsPrimary = true });

        _db.VendorAddresses.AddRange(
            new VendorAddress { Id = 30, VendorId = 1, Label = "Remit", AddressType = VendorAddressType.RemitTo, IsDefault = true, Line1 = "1 Pay St", City = "Gary", State = "IN", PostalCode = "46402" },
            new VendorAddress { Id = 31, VendorId = 1, Label = "Warehouse", AddressType = VendorAddressType.OrderFrom, Line1 = "2 Dock Rd", City = "Gary", State = "IN", PostalCode = "46403" },
            new VendorAddress { Id = 32, VendorId = 1, Label = "Sales office", AddressType = VendorAddressType.OrderFrom, IsDefault = true, Line1 = "3 Order Ave", Line2 = "Suite 4", City = "Gary", State = "IN", PostalCode = "46404" },
            new VendorAddress { Id = 33, VendorId = 1, Label = "Closed", AddressType = VendorAddressType.OrderFrom, IsDefault = true, IsActive = false, Line1 = "9 Gone Way", City = "Gary", State = "IN", PostalCode = "46405" },
            new VendorAddress { Id = 40, VendorId = 2, Label = "Other", AddressType = VendorAddressType.OrderFrom, IsDefault = true, Line1 = "5 Else St", City = "Elsewhere", State = "OH", PostalCode = "44101" });

        _db.CompanyLocations.AddRange(
            new CompanyLocation { Id = 50, Name = "North Plant", Line1 = "10 North Rd", City = "Ogden", State = "UT", PostalCode = "84401" },
            new CompanyLocation { Id = 51, Name = "Main Plant", Line1 = "1 Factory Way", City = "Ogden", State = "UT", PostalCode = "84402", IsDefault = true },
            new CompanyLocation { Id = 52, Name = "Closed Plant", Line1 = "0 Shut Rd", City = "Ogden", State = "UT", PostalCode = "84403", IsActive = false });

        await _db.SaveChangesAsync();
    }

    private static CreatePurchaseOrderCommand Command(int? contactId = null, int? addressId = null, int? shipToId = null) =>
        new(1, null, null, [new CreatePurchaseOrderLineModel(null, "Saw blade sharpening", 1, 60m, null)],
            VendorContactId: contactId, VendorAddressId: addressId, ShipToLocationId: shipToId);

    [Fact]
    public async Task Create_without_picks_defaults_to_the_primary_contact_order_from_address_and_default_location()
    {
        await SeedAsync();

        await CreateHandler().Handle(Command(), CancellationToken.None);

        _added!.VendorContactId.Should().Be(12);
        _added.VendorAddressId.Should().Be(32);
        _added.ShipToLocationId.Should().Be(51);
        _db.ActivityLogs.Should().ContainSingle(a => a.EntityType == "PurchaseOrder" && a.Action == "created"
            && a.Description.Contains("attn Jane Doe")
            && a.Description.Contains("order from Sales office")
            && a.Description.Contains("ship to Main Plant"));
    }

    [Fact]
    public async Task Create_falls_back_to_the_remit_to_address_when_the_vendor_has_no_order_from_address()
    {
        await SeedAsync();
        _db.VendorAddresses.RemoveRange(_db.VendorAddresses.Where(a => a.AddressType == VendorAddressType.OrderFrom && a.VendorId == 1));
        await _db.SaveChangesAsync();

        await CreateHandler().Handle(Command(), CancellationToken.None);

        _added!.VendorAddressId.Should().Be(30);
    }

    [Fact]
    public async Task Create_leaves_the_picks_empty_when_nothing_is_on_file()
    {
        _db.Vendors.Add(new Vendor { Id = 1, CompanyName = "Bare Vendor" });
        await _db.SaveChangesAsync();
        _vendorRepo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(await _db.Vendors.FindAsync(1));

        await CreateHandler().Handle(Command(), CancellationToken.None);

        _added!.VendorContactId.Should().BeNull();
        _added.VendorAddressId.Should().BeNull();
        _added.ShipToLocationId.Should().BeNull();
    }

    [Fact]
    public async Task Create_with_explicit_picks_uses_them()
    {
        await SeedAsync();

        await CreateHandler().Handle(Command(contactId: 10, addressId: 31, shipToId: 50), CancellationToken.None);

        _added!.VendorContactId.Should().Be(10);
        _added.VendorAddressId.Should().Be(31);
        _added.ShipToLocationId.Should().Be(50);
    }

    [Fact]
    public async Task Create_rejects_a_contact_and_address_from_another_vendor_and_an_inactive_location()
    {
        await SeedAsync();

        var act = () => CreateHandler().Handle(Command(contactId: 20, addressId: 40, shipToId: 52), CancellationToken.None);

        var error = await act.Should().ThrowAsync<ValidationException>();
        error.Which.Errors.Select(e => e.PropertyName).Should()
            .BeEquivalentTo(["vendorContactId", "vendorAddressId", "shipToLocationId"]);
        _added.Should().BeNull();
    }

    [Fact]
    public async Task Create_rejects_an_inactive_contact_and_address_of_the_same_vendor()
    {
        await SeedAsync();

        var act = () => CreateHandler().Handle(Command(contactId: 11, addressId: 33), CancellationToken.None);

        var error = await act.Should().ThrowAsync<ValidationException>();
        error.Which.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["vendorContactId", "vendorAddressId"]);
    }

    [Fact]
    public async Task Update_in_draft_changes_the_picks_and_records_them_in_one_activity_row()
    {
        await SeedAsync();
        var po = new PurchaseOrder { Id = 7, PONumber = "PO-00007", VendorId = 1, Status = PurchaseOrderStatus.Draft, VendorContactId = 12 };
        _poRepo.Setup(r => r.FindAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(po);

        await UpdateHandler().Handle(
            new UpdatePurchaseOrderCommand(7, null, null, VendorContactId: 10, VendorAddressId: 31, ShipToLocationId: 50),
            CancellationToken.None);

        po.VendorContactId.Should().Be(10);
        po.VendorAddressId.Should().Be(31);
        po.ShipToLocationId.Should().Be(50);
        _db.ActivityLogs.Local.Should().ContainSingle(a => a.EntityType == "PurchaseOrder" && a.Action == "updated"
            && a.Description.Contains("vendorContact, vendorAddress, shipToLocation")
            && a.Description.Contains("attn Sam Sales"));
    }

    [Fact]
    public async Task Update_rejects_a_contact_from_another_vendor()
    {
        await SeedAsync();
        var po = new PurchaseOrder { Id = 7, PONumber = "PO-00007", VendorId = 1, Status = PurchaseOrderStatus.Draft, VendorContactId = 12 };
        _poRepo.Setup(r => r.FindAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(po);

        var act = () => UpdateHandler().Handle(new UpdatePurchaseOrderCommand(7, null, null, VendorContactId: 20), CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors
            .Should().ContainSingle(e => e.PropertyName == "vendorContactId");
        po.VendorContactId.Should().Be(12);
    }

    [Fact]
    public async Task Update_after_draft_refuses_to_change_the_picks_but_accepts_the_same_values()
    {
        await SeedAsync();
        var po = new PurchaseOrder { Id = 7, PONumber = "PO-00007", VendorId = 1, Status = PurchaseOrderStatus.Submitted, VendorContactId = 12, ShipToLocationId = 51 };
        _poRepo.Setup(r => r.FindAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(po);

        var change = () => UpdateHandler().Handle(new UpdatePurchaseOrderCommand(7, null, null, ShipToLocationId: 50), CancellationToken.None);
        await change.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Draft*");

        await UpdateHandler().Handle(new UpdatePurchaseOrderCommand(7, null, null, VendorContactId: 12, ShipToLocationId: 51), CancellationToken.None);
        po.ShipToLocationId.Should().Be(51);
    }

    [Fact]
    public async Task Detail_returns_the_contact_the_address_and_the_ship_to()
    {
        await SeedAsync();
        var po = new PurchaseOrder
        {
            Id = 7, PONumber = "PO-00007", VendorId = 1, Vendor = (await _db.Vendors.FindAsync(1))!,
            VendorContactId = 12, VendorAddressId = 32, ShipToLocationId = 51,
        };
        _poRepo.Setup(r => r.FindWithDetailsAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(po);

        var result = await new GetPurchaseOrderByIdHandler(_poRepo.Object, _db)
            .Handle(new GetPurchaseOrderByIdQuery(7), CancellationToken.None);

        result.VendorContactId.Should().Be(12);
        result.VendorContactName.Should().Be("Jane Doe");
        result.VendorContactEmail.Should().Be("jane@example.test");
        result.VendorContactPhone.Should().Be("555-0110");
        result.VendorContactFax.Should().Be("555-0111");
        result.VendorAddressId.Should().Be(32);
        result.VendorAddressType.Should().Be("OrderFrom");
        result.VendorAddressLabel.Should().Be("Sales office");
        result.VendorAddressText.Should().Be("3 Order Ave, Suite 4, Gary, IN 46404, US");
        result.ShipToLocationId.Should().Be(51);
        result.ShipToLocationName.Should().Be("Main Plant");
        result.ShipToAddressText.Should().Be("1 Factory Way, Ogden, UT 84402, US");
    }

    [Fact]
    public async Task Pdf_prints_the_chosen_contact_with_fax_the_order_from_address_and_the_ship_to()
    {
        await SeedAsync();
        var po = new PurchaseOrder
        {
            Id = 7, PONumber = "PO-00007", VendorId = 1, Status = PurchaseOrderStatus.Submitted,
            VendorContactId = 12, VendorAddressId = 32, ShipToLocationId = 50,
        };
        po.Lines.Add(new PurchaseOrderLine { Id = 70, Description = "Saw blade sharpening", OrderedQuantity = 1, UnitPrice = 60m });
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();

        var document = await new GetPurchaseOrderPdfHandler(_db).BuildDocumentAsync(7, CancellationToken.None);

        document.VendorContactLines().Should().Equal(
            "Attn: Jane Doe", "Phone: 555-0110", "Fax: 555-0111", "Email: jane@example.test");
        document.VendorAddressLines().Should().Equal("3 Order Ave", "Suite 4", "Gary, IN 46404", "US");
        document.ShipTo!.Name.Should().Be("North Plant");
        (await new GetPurchaseOrderPdfHandler(_db).Handle(new GetPurchaseOrderPdfQuery(7), CancellationToken.None))
            .Should().NotBeEmpty();
    }

    [Fact]
    public async Task Pdf_falls_back_to_the_vendor_fields_and_prints_the_vendor_fax()
    {
        _db.Vendors.Add(new Vendor
        {
            Id = 3, CompanyName = "Plain Vendor", ContactName = "Pat Buyer", Phone = "555-0100", Fax = "555-0199",
            Address = "100 Mill Road", City = "Gary", State = "IN", ZipCode = "46402",
        });
        _db.CompanyLocations.Add(new CompanyLocation { Id = 51, Name = "Main Plant", Line1 = "1 Factory Way", City = "Ogden", State = "UT", PostalCode = "84402", IsDefault = true });
        var po = new PurchaseOrder { Id = 8, PONumber = "PO-00008", VendorId = 3, Status = PurchaseOrderStatus.Draft };
        po.Lines.Add(new PurchaseOrderLine { Id = 80, Description = "Consulting", OrderedQuantity = 1, UnitPrice = 10m });
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();

        var document = await new GetPurchaseOrderPdfHandler(_db).BuildDocumentAsync(8, CancellationToken.None);

        document.VendorContactLines().Should().Equal("Attn: Pat Buyer", "Phone: 555-0100", "Fax: 555-0199");
        document.VendorAddressLines().Should().Equal("100 Mill Road", "Gary, IN 46402");
        document.ShipTo!.Name.Should().Be("Main Plant");
    }
}
