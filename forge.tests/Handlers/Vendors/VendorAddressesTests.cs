using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Vendors.Addresses;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Vendors;

public class VendorAddressesTests
{
    private static async Task<Vendor> SeedVendorAsync(AppDbContext db)
    {
        var vendor = new Vendor { CompanyName = "Acme Supply" };
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();
        return vendor;
    }

    private static CreateVendorAddressCommand NewAddress(int vendorId, string label, string type, bool isDefault = false) =>
        new(vendorId, label, type, "1 Main St", null, "Springfield", "IL", "62701", "US", isDefault);

    private static UpdateVendorAddressCommand UpdateOf(int vendorId, int addressId, CreateVendorAddressCommand c) =>
        new(vendorId, addressId, c.Label, c.AddressType, c.Line1, c.Line2, c.City, c.State, c.PostalCode, c.Country, c.IsDefault, c.IsActive);

    private static Task<List<string>> DefaultLabelsAsync(AppDbContext db) =>
        db.VendorAddresses.Where(a => a.IsDefault).OrderBy(a => a.Label).Select(a => a.Label).ToListAsync();

    [Fact]
    public async Task A_vendor_holds_remit_to_and_order_from_addresses()
    {
        using var db = TestDbContextFactory.Create();
        var vendor = await SeedVendorAsync(db);
        var create = new CreateVendorAddressHandler(db);

        await create.Handle(NewAddress(vendor.Id, "Lockbox", "RemitTo", true), CancellationToken.None);
        await create.Handle(NewAddress(vendor.Id, "Sales office", "orderfrom", true), CancellationToken.None);
        await create.Handle(NewAddress(vendor.Id, "Plant 2", "ShipFrom"), CancellationToken.None);

        var list = await new GetVendorAddressesHandler(db).Handle(new GetVendorAddressesQuery(vendor.Id), CancellationToken.None);

        list.Select(a => (a.Label, a.AddressType)).Should().Equal(
            ("Lockbox", "RemitTo"), ("Sales office", "OrderFrom"), ("Plant 2", "ShipFrom"));
        (await DefaultLabelsAsync(db)).Should().Equal("Lockbox", "Sales office");
    }

    [Fact]
    public async Task Create_writes_a_vendor_activity_row_naming_the_type()
    {
        using var db = TestDbContextFactory.Create();
        var vendor = await SeedVendorAsync(db);

        await new CreateVendorAddressHandler(db).Handle(NewAddress(vendor.Id, "Lockbox", "RemitTo"), CancellationToken.None);

        var row = await db.ActivityLogs.SingleAsync(a => a.Action == "address-added");
        row.EntityType.Should().Be("Vendor");
        row.EntityId.Should().Be(vendor.Id);
        row.Action.Should().Be("address-added");
        row.Description.Should().StartWith("Remit-to address added: Lockbox");
    }

    [Fact]
    public async Task A_new_default_clears_only_the_same_type()
    {
        using var db = TestDbContextFactory.Create();
        var vendor = await SeedVendorAsync(db);
        var create = new CreateVendorAddressHandler(db);
        await create.Handle(NewAddress(vendor.Id, "Old remit", "RemitTo", true), CancellationToken.None);
        await create.Handle(NewAddress(vendor.Id, "Order desk", "OrderFrom", true), CancellationToken.None);

        await create.Handle(NewAddress(vendor.Id, "New remit", "RemitTo", true), CancellationToken.None);

        (await DefaultLabelsAsync(db)).Should().Equal("New remit", "Order desk");
        (await db.ActivityLogs.Where(a => a.Action == "address-added").OrderBy(a => a.Id).LastAsync()).Description.Should().Contain("replaces Old remit as default");
    }

    [Fact]
    public async Task Update_changes_fields_moves_the_default_and_logs_one_rollup_row()
    {
        using var db = TestDbContextFactory.Create();
        var vendor = await SeedVendorAsync(db);
        var create = new CreateVendorAddressHandler(db);
        await create.Handle(NewAddress(vendor.Id, "Old remit", "RemitTo", true), CancellationToken.None);
        var created = NewAddress(vendor.Id, "Lockbox", "RemitTo");
        var target = await create.Handle(created, CancellationToken.None);

        var result = await new UpdateVendorAddressHandler(db).Handle(
            UpdateOf(vendor.Id, target.Id, created with { Line1 = "PO Box 9", City = "Peoria", IsDefault = true }),
            CancellationToken.None);

        result.Line1.Should().Be("PO Box 9");
        (await DefaultLabelsAsync(db)).Should().Equal("Lockbox");
        var row = await db.ActivityLogs.SingleAsync(a => a.Action == "address-updated");
        row.EntityType.Should().Be("Vendor");
        row.Description.Should().StartWith("Remit-to address updated: Lockbox")
            .And.Contain("3 fields: line1, city, set-default")
            .And.Contain("replaces Old remit as default");
    }

    [Fact]
    public async Task Changing_a_default_address_type_clears_the_default_of_the_new_type()
    {
        using var db = TestDbContextFactory.Create();
        var vendor = await SeedVendorAsync(db);
        var create = new CreateVendorAddressHandler(db);
        await create.Handle(NewAddress(vendor.Id, "Order desk", "OrderFrom", true), CancellationToken.None);
        var created = NewAddress(vendor.Id, "Lockbox", "RemitTo", true);
        var target = await create.Handle(created, CancellationToken.None);

        await new UpdateVendorAddressHandler(db).Handle(
            UpdateOf(vendor.Id, target.Id, created with { AddressType = "OrderFrom" }), CancellationToken.None);

        (await DefaultLabelsAsync(db)).Should().Equal("Lockbox");
    }

    [Fact]
    public async Task Update_with_no_changes_writes_no_activity()
    {
        using var db = TestDbContextFactory.Create();
        var vendor = await SeedVendorAsync(db);
        var created = NewAddress(vendor.Id, "Lockbox", "RemitTo");
        var target = await new CreateVendorAddressHandler(db).Handle(created, CancellationToken.None);

        await new UpdateVendorAddressHandler(db).Handle(UpdateOf(vendor.Id, target.Id, created), CancellationToken.None);

        (await db.ActivityLogs.CountAsync(a => a.Action == "address-updated")).Should().Be(0);
    }

    [Fact]
    public async Task Delete_is_soft_hides_the_address_and_logs_on_the_vendor()
    {
        using var db = TestDbContextFactory.Create();
        var vendor = await SeedVendorAsync(db);
        var target = await new CreateVendorAddressHandler(db).Handle(NewAddress(vendor.Id, "Lockbox", "RemitTo"), CancellationToken.None);
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));

        await new DeleteVendorAddressHandler(db, clock.Object).Handle(
            new DeleteVendorAddressCommand(vendor.Id, target.Id), CancellationToken.None);

        (await new GetVendorAddressesHandler(db).Handle(new GetVendorAddressesQuery(vendor.Id, true), CancellationToken.None))
            .Should().BeEmpty();
        (await db.VendorAddresses.IgnoreQueryFilters().SingleAsync(a => a.Id == target.Id)).DeletedAt.Should().NotBeNull();
        var row = await db.ActivityLogs.SingleAsync(a => a.Action == "address-removed");
        row.EntityId.Should().Be(vendor.Id);
        row.Description.Should().Be("Remit-to address removed: Lockbox");
    }

    [Fact]
    public async Task Delete_of_an_address_on_another_vendor_is_not_found()
    {
        using var db = TestDbContextFactory.Create();
        var vendor = await SeedVendorAsync(db);
        var other = await SeedVendorAsync(db);
        var target = await new CreateVendorAddressHandler(db).Handle(NewAddress(vendor.Id, "Lockbox", "RemitTo"), CancellationToken.None);

        var act = () => new DeleteVendorAddressHandler(db, Mock.Of<IClock>()).Handle(
            new DeleteVendorAddressCommand(other.Id, target.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Inactive_addresses_are_hidden_unless_asked_for()
    {
        using var db = TestDbContextFactory.Create();
        var vendor = await SeedVendorAsync(db);
        var create = new CreateVendorAddressHandler(db);
        await create.Handle(NewAddress(vendor.Id, "Lockbox", "RemitTo"), CancellationToken.None);
        await create.Handle(NewAddress(vendor.Id, "Old plant", "ShipFrom") with { IsActive = false }, CancellationToken.None);
        var get = new GetVendorAddressesHandler(db);

        (await get.Handle(new GetVendorAddressesQuery(vendor.Id), CancellationToken.None)).Should().ContainSingle();
        (await get.Handle(new GetVendorAddressesQuery(vendor.Id, true), CancellationToken.None)).Should().HaveCount(2);
    }

    [Theory]
    [InlineData("RemitTo", "IL", "US", true)]
    [InlineData("other", "Ontario", "CA", true)]
    [InlineData("Shipping", "IL", "US", false)]
    [InlineData("7", "IL", "US", false)]
    [InlineData("1", "IL", "US", false)]
    [InlineData("RemitTo", "Illinois", "US", false)]
    [InlineData("RemitTo", "I1", "us", false)]
    public void Validator_checks_the_type_and_us_state_code(string type, string state, string country, bool valid)
    {
        var command = new CreateVendorAddressCommand(1, "Main", type, "1 Main St", null, "Springfield", state, "62701", country, false);

        new CreateVendorAddressValidator().Validate(command).IsValid.Should().Be(valid);
        new UpdateVendorAddressValidator().Validate(UpdateOf(1, 1, command)).IsValid.Should().Be(valid);
    }

    [Fact]
    public void Validator_rejects_values_longer_than_the_columns()
    {
        var command = new CreateVendorAddressCommand(1, new string('x', 101), "RemitTo", "1 Main St", null,
            "Springfield", "IL", new string('9', 21), "US", false);

        new CreateVendorAddressValidator().Validate(command).Errors.Select(e => e.PropertyName)
            .Should().BeEquivalentTo(["Label", "PostalCode"]);
    }

    [Fact]
    public void Every_address_type_has_a_readable_name()
    {
        Enum.GetValues<VendorAddressType>().Select(VendorAddressRules.Describe)
            .Should().Equal("Remit-to", "Order-from", "Ship-from", "Billing", "Other");
    }
}
