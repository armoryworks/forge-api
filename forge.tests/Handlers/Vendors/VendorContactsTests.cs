using System.Reflection;

using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Controllers;
using Forge.Api.Features.Vendors.Contacts;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Vendors;

public class VendorContactsTests
{
    private static async Task<Vendor> SeedVendorAsync(AppDbContext db)
    {
        var vendor = new Vendor { CompanyName = "Acme Supply" };
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();
        return vendor;
    }

    private static CreateVendorContactCommand NewContact(int vendorId, string first, string last, bool isPrimary = false, string? role = null) =>
        new(vendorId, first, last, $"{first.ToLowerInvariant()}@acme.test", "555-0100", null, null, role, isPrimary, null);

    private static Task<List<string>> PrimaryNamesAsync(AppDbContext db) =>
        db.VendorContacts.Where(c => c.IsPrimary).Select(c => c.FirstName).ToListAsync();

    [Fact]
    public async Task A_vendor_holds_several_contacts_and_lists_the_primary_first()
    {
        using var db = TestDbContextFactory.Create();
        var vendor = await SeedVendorAsync(db);
        var create = new CreateVendorContactHandler(db);

        await create.Handle(NewContact(vendor.Id, "Zed", "Adams", role: "Sales"), CancellationToken.None);
        await create.Handle(NewContact(vendor.Id, "Amy", "Brown", isPrimary: true, role: "AP"), CancellationToken.None);
        await create.Handle(NewContact(vendor.Id, "Bob", "Clark"), CancellationToken.None);

        var list = await new GetVendorContactsHandler(db).Handle(new GetVendorContactsQuery(vendor.Id), CancellationToken.None);

        list.Select(c => c.FirstName).Should().Equal("Amy", "Zed", "Bob");
        list.Should().OnlyContain(c => c.VendorId == vendor.Id);
    }

    [Fact]
    public async Task Create_writes_a_vendor_activity_row_naming_the_contact_and_role()
    {
        using var db = TestDbContextFactory.Create();
        var vendor = await SeedVendorAsync(db);

        await new CreateVendorContactHandler(db).Handle(NewContact(vendor.Id, "Jane", "Doe", role: "Sales"), CancellationToken.None);

        var row = await db.ActivityLogs.SingleAsync(a => a.Action == "contact-added");
        row.EntityType.Should().Be("Vendor");
        row.EntityId.Should().Be(vendor.Id);
        row.Action.Should().Be("contact-added");
        row.Description.Should().StartWith("Contact added: Jane Doe (Sales)");
    }

    [Fact]
    public async Task A_new_primary_contact_clears_the_previous_primary()
    {
        using var db = TestDbContextFactory.Create();
        var vendor = await SeedVendorAsync(db);
        var create = new CreateVendorContactHandler(db);
        await create.Handle(NewContact(vendor.Id, "Old", "Primary", isPrimary: true), CancellationToken.None);

        await create.Handle(NewContact(vendor.Id, "New", "Primary", isPrimary: true), CancellationToken.None);

        (await PrimaryNamesAsync(db)).Should().Equal("New");
        (await db.ActivityLogs.Where(a => a.Action == "contact-added").OrderBy(a => a.Id).LastAsync()).Description.Should().Contain("replaces Old Primary as primary");
    }

    [Fact]
    public async Task Update_patches_fields_moves_the_primary_and_logs_one_rollup_row()
    {
        using var db = TestDbContextFactory.Create();
        var vendor = await SeedVendorAsync(db);
        var create = new CreateVendorContactHandler(db);
        await create.Handle(NewContact(vendor.Id, "Old", "Primary", isPrimary: true), CancellationToken.None);
        var target = await create.Handle(NewContact(vendor.Id, "Jane", "Doe"), CancellationToken.None);

        var result = await new UpdateVendorContactHandler(db).Handle(
            new UpdateVendorContactCommand(vendor.Id, target.Id, null, null, "jane.doe@acme.test", null, "555-0199",
                null, null, true, null, null),
            CancellationToken.None);

        result.Email.Should().Be("jane.doe@acme.test");
        result.Mobile.Should().Be("555-0199");
        result.Phone.Should().Be("555-0100");
        (await PrimaryNamesAsync(db)).Should().Equal("Jane");
        var row = await db.ActivityLogs.SingleAsync(a => a.Action == "contact-updated");
        row.EntityType.Should().Be("Vendor");
        row.EntityId.Should().Be(vendor.Id);
        row.Description.Should().Contain("3 fields: email, mobile, set-primary").And.Contain("replaces Old Primary as primary");
    }

    [Fact]
    public async Task Update_with_an_empty_string_clears_an_optional_field()
    {
        using var db = TestDbContextFactory.Create();
        var vendor = await SeedVendorAsync(db);
        var target = await new CreateVendorContactHandler(db).Handle(NewContact(vendor.Id, "Jane", "Doe"), CancellationToken.None);

        var result = await new UpdateVendorContactHandler(db).Handle(
            new UpdateVendorContactCommand(vendor.Id, target.Id, null, null, null, "", null, null, null, null, null, null),
            CancellationToken.None);

        result.Phone.Should().BeNull();
    }

    [Fact]
    public async Task Update_with_no_changes_writes_no_activity()
    {
        using var db = TestDbContextFactory.Create();
        var vendor = await SeedVendorAsync(db);
        var target = await new CreateVendorContactHandler(db).Handle(NewContact(vendor.Id, "Jane", "Doe"), CancellationToken.None);

        await new UpdateVendorContactHandler(db).Handle(
            new UpdateVendorContactCommand(vendor.Id, target.Id, "Jane", null, null, null, null, null, null, false, null, null),
            CancellationToken.None);

        (await db.ActivityLogs.CountAsync(a => a.Action == "contact-updated")).Should().Be(0);
    }

    [Fact]
    public async Task Update_of_a_contact_on_another_vendor_is_not_found()
    {
        using var db = TestDbContextFactory.Create();
        var vendor = await SeedVendorAsync(db);
        var other = await SeedVendorAsync(db);
        var target = await new CreateVendorContactHandler(db).Handle(NewContact(vendor.Id, "Jane", "Doe"), CancellationToken.None);

        var act = () => new UpdateVendorContactHandler(db).Handle(
            new UpdateVendorContactCommand(other.Id, target.Id, "X", null, null, null, null, null, null, null, null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Delete_is_soft_hides_the_contact_and_logs_on_the_vendor()
    {
        using var db = TestDbContextFactory.Create();
        var vendor = await SeedVendorAsync(db);
        var target = await new CreateVendorContactHandler(db).Handle(NewContact(vendor.Id, "Jane", "Doe", role: "Sales"), CancellationToken.None);
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));

        await new DeleteVendorContactHandler(db, clock.Object).Handle(
            new DeleteVendorContactCommand(vendor.Id, target.Id), CancellationToken.None);

        var list = await new GetVendorContactsHandler(db).Handle(new GetVendorContactsQuery(vendor.Id, true), CancellationToken.None);
        list.Should().BeEmpty();
        var stored = await db.VendorContacts.IgnoreQueryFilters().SingleAsync(c => c.Id == target.Id);
        stored.DeletedAt.Should().Be(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
        var row = await db.ActivityLogs.SingleAsync(a => a.Action == "contact-removed");
        row.EntityId.Should().Be(vendor.Id);
        row.Description.Should().Be("Contact removed: Jane Doe (Sales)");
    }

    [Fact]
    public async Task Inactive_contacts_are_hidden_unless_asked_for()
    {
        using var db = TestDbContextFactory.Create();
        var vendor = await SeedVendorAsync(db);
        var create = new CreateVendorContactHandler(db);
        await create.Handle(NewContact(vendor.Id, "Active", "One"), CancellationToken.None);
        await create.Handle(NewContact(vendor.Id, "Gone", "Two") with { IsActive = false }, CancellationToken.None);
        var get = new GetVendorContactsHandler(db);

        (await get.Handle(new GetVendorContactsQuery(vendor.Id), CancellationToken.None)).Should().ContainSingle();
        (await get.Handle(new GetVendorContactsQuery(vendor.Id, true), CancellationToken.None)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Contacts_on_a_missing_vendor_are_not_found()
    {
        using var db = TestDbContextFactory.Create();

        var act = () => new CreateVendorContactHandler(db).Handle(NewContact(999, "Jane", "Doe"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData("not-an-email", null)]
    [InlineData(null, 51)]
    public void Create_validator_rejects_bad_email_and_overlong_mobile(string? email, int? mobileLength)
    {
        var command = new CreateVendorContactCommand(1, "Jane", "Doe", email, null,
            mobileLength is null ? null : new string('5', mobileLength.Value), null, null, false, null);

        new CreateVendorContactValidator().Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validators_accept_a_well_formed_contact_and_reject_a_blank_name_patch()
    {
        new CreateVendorContactValidator().Validate(
            new CreateVendorContactCommand(1, "Jane", "Doe", "jane@acme.test", "555", "555", "555", "Sales", true, "notes"))
            .IsValid.Should().BeTrue();
        new UpdateVendorContactValidator().Validate(
            new UpdateVendorContactCommand(1, 1, "", null, null, null, null, null, null, null, null, null))
            .IsValid.Should().BeFalse();
        new UpdateVendorContactValidator().Validate(
            new UpdateVendorContactCommand(1, 1, null, null, "bad", null, null, null, null, null, null, null))
            .IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(typeof(VendorContactsController))]
    [InlineData(typeof(VendorAddressesController))]
    public void Controllers_share_the_vendor_roles_and_capability_gate(Type controller)
    {
        controller.GetCustomAttribute<RequiresCapabilityAttribute>()!.Capability.Should().Be("CAP-MD-VENDORS");
        controller.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("Admin,Manager,OfficeManager");
    }
}
