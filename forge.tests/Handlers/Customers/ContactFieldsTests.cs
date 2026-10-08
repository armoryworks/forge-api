using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.CustomerAddresses;
using Forge.Api.Features.Customers;
using Forge.Api.Features.Vendors;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Customers;

public class ContactFieldsTests
{
    [Fact]
    public async Task A_contact_mobile_number_round_trips_through_create_update_and_detail()
    {
        using var db = TestDbContextFactory.Create();
        var customer = new Customer { Name = "Acme" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var created = await new CreateContactHandler(new CustomerRepository(db), db).Handle(
            new CreateContactCommand(customer.Id, "Ada", "Byron", null, "555-0100", null, false, Mobile: "555-0111"),
            CancellationToken.None);
        created.Mobile.Should().Be("555-0111");

        var updated = await new UpdateContactHandler(db).Handle(
            new UpdateContactCommand(customer.Id, created.Id, null, null, null, null, null, null, Mobile: "555-0122"),
            CancellationToken.None);
        updated.Mobile.Should().Be("555-0122");
        updated.Phone.Should().Be("555-0100");

        var detail = await new GetCustomerByIdHandler(new CustomerRepository(db))
            .Handle(new GetCustomerByIdQuery(customer.Id), CancellationToken.None);
        detail.Contacts.Single().Mobile.Should().Be("555-0122");
        (await db.ActivityLogs.AnyAsync(a => a.Action == "contact-updated" && a.Description.Contains("mobile")))
            .Should().BeTrue();
    }

    [Fact]
    public void Contact_validators_reject_a_mobile_number_longer_than_the_column()
    {
        var tooLong = new string('5', 51);

        new CreateContactValidator()
            .Validate(new CreateContactCommand(1, "Ada", "Byron", null, null, null, false, Mobile: tooLong))
            .IsValid.Should().BeFalse();
        new UpdateContactValidator()
            .Validate(new UpdateContactCommand(1, 1, null, null, null, null, null, null, Mobile: tooLong))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task An_address_dock_contact_and_phone_round_trip_through_create_update_and_list()
    {
        using var db = TestDbContextFactory.Create();
        var customer = new Customer { Name = "Acme" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var addressRepo = new CustomerAddressRepository(db);

        var created = await new CreateCustomerAddressHandler(addressRepo, new CustomerRepository(db), db).Handle(
            new CreateCustomerAddressCommand(customer.Id, "Dock 4", "Shipping", "1 Main St", null,
                "Springfield", "IL", "62701", "US", false, ContactName: "Receiving desk", Phone: "555-0100"),
            CancellationToken.None);
        created.ContactName.Should().Be("Receiving desk");
        created.Phone.Should().Be("555-0100");

        await new UpdateCustomerAddressHandler(addressRepo, db).Handle(
            new UpdateCustomerAddressCommand(created.Id, "Dock 4", "Shipping", "1 Main St", null,
                "Springfield", "IL", "62701", "US", false, ContactName: "Night shift lead", Phone: "555-0199"),
            CancellationToken.None);

        var listed = (await addressRepo.GetByCustomerAsync(customer.Id, CancellationToken.None)).Single();
        listed.ContactName.Should().Be("Night shift lead");
        listed.Phone.Should().Be("555-0199");
        (await db.ActivityLogs.SingleAsync(a => a.Action == "address-updated")).Description
            .Should().Contain("contactName").And.Contain("phone");
    }

    [Fact]
    public async Task An_empty_string_clears_a_contact_fax_and_phone_while_null_leaves_them()
    {
        using var db = TestDbContextFactory.Create();
        var customer = new Customer { Name = "Acme" };
        var contact = new Contact
        {
            FirstName = "Ada", LastName = "Byron",
            Phone = "555-0100", Mobile = "555-0111", Fax = "555-0199", Email = "ada@example.com",
        };
        customer.Contacts.Add(contact);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var handler = new UpdateContactHandler(db);

        await handler.Handle(
            new UpdateContactCommand(customer.Id, contact.Id, null, null, null, null, null, null),
            CancellationToken.None);
        var unchanged = await db.Contacts.AsNoTracking().SingleAsync();
        unchanged.Phone.Should().Be("555-0100");
        unchanged.Fax.Should().Be("555-0199");

        var result = await handler.Handle(
            new UpdateContactCommand(customer.Id, contact.Id, null, null, "", "", null, null, Fax: "", Mobile: ""),
            CancellationToken.None);

        result.Phone.Should().BeNull();
        result.Fax.Should().BeNull();
        result.Mobile.Should().BeNull();
        result.Email.Should().BeNull();
        var stored = await db.Contacts.AsNoTracking().SingleAsync();
        stored.Phone.Should().BeNull();
        stored.Fax.Should().BeNull();
    }

    [Fact]
    public async Task An_empty_string_clears_a_customer_fax_and_phone()
    {
        using var db = TestDbContextFactory.Create();
        var customer = new Customer { Name = "Acme", Phone = "555-0100", Fax = "555-0199" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        await new UpdateCustomerHandler(
                new CustomerRepository(db), Mock.Of<ISystemSettingRepository>(), Mock.Of<IBusinessIdentifierService>(),
                db, Mock.Of<IClock>())
            .Handle(new UpdateCustomerCommand(customer.Id, null, null, null, "", null, Fax: ""), CancellationToken.None);

        var stored = await db.Customers.AsNoTracking().SingleAsync();
        stored.Phone.Should().BeNull();
        stored.Fax.Should().BeNull();
    }

    [Fact]
    public async Task UpdateVendor_clears_fax_and_phone_and_writes_one_activity_row()
    {
        using var db = TestDbContextFactory.Create();
        var vendor = new Vendor { CompanyName = "Acme", Phone = "555-0100", Fax = "555-0199", Notes = "old" };
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();

        await new UpdateVendorHandler(
                new VendorRepository(db), Mock.Of<ISystemSettingRepository>(), Mock.Of<IBusinessIdentifierService>(),
                db, Mock.Of<IClock>())
            .Handle(
                new UpdateVendorCommand(vendor.Id, null, null, null, "", null, null, null, null, null, null, "new", null, null, Fax: ""),
                CancellationToken.None);

        var stored = await db.Vendors.AsNoTracking().SingleAsync();
        stored.Phone.Should().BeNull();
        stored.Fax.Should().BeNull();
        stored.Notes.Should().Be("new");
        var rows = await db.ActivityLogs
            .Where(a => a.EntityType == "Vendor" && a.EntityId == vendor.Id && a.Action == "updated")
            .ToListAsync();
        rows.Should().ContainSingle();
        rows[0].Description.Should().Contain("phone").And.Contain("fax").And.Contain("notes");
    }

    [Fact]
    public async Task UpdateVendor_writes_no_activity_row_when_nothing_changed()
    {
        using var db = TestDbContextFactory.Create();
        var vendor = new Vendor { CompanyName = "Acme", Phone = "555-0100" };
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();

        await new UpdateVendorHandler(
                new VendorRepository(db), Mock.Of<ISystemSettingRepository>(), Mock.Of<IBusinessIdentifierService>(),
                db, Mock.Of<IClock>())
            .Handle(
                new UpdateVendorCommand(vendor.Id, "Acme", null, null, "555-0100", null, null, null, null, null, null, null, null, null),
                CancellationToken.None);

        (await db.ActivityLogs.AnyAsync(a => a.EntityType == "Vendor" && a.Action == "updated")).Should().BeFalse();
    }
}
