using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.CustomerAddresses;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.CustomerAddresses;

public class DefaultAddressTests
{
    private static CustomerAddress Address(string label, AddressType type, bool isDefault) => new()
    {
        Label = label, AddressType = type, Line1 = "1 Main St",
        City = "Springfield", State = "IL", PostalCode = "62701", IsDefault = isDefault,
    };

    private static async Task<Customer> SeedAsync(AppDbContext db, params CustomerAddress[] addresses)
    {
        var customer = new Customer { Name = "Acme" };
        foreach (var address in addresses)
            customer.Addresses.Add(address);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer;
    }

    private static Task<List<string>> DefaultLabelsAsync(AppDbContext db) =>
        db.CustomerAddresses.Where(a => a.IsDefault).OrderBy(a => a.Label).Select(a => a.Label).ToListAsync();

    private static CreateCustomerAddressHandler CreateHandler(AppDbContext db) =>
        new(new CustomerAddressRepository(db), new CustomerRepository(db), db);

    [Fact]
    public async Task A_new_default_billing_address_clears_billing_and_both_defaults_only()
    {
        using var db = TestDbContextFactory.Create();
        var customer = await SeedAsync(db,
            Address("OldBilling", AddressType.Billing, true),
            Address("OldShipping", AddressType.Shipping, true),
            Address("OldBoth", AddressType.Both, true));

        await CreateHandler(db).Handle(
            new CreateCustomerAddressCommand(customer.Id, "NewBilling", "Billing", "2 Elm St", null,
                "Springfield", "IL", "62701", "US", true),
            CancellationToken.None);

        (await DefaultLabelsAsync(db)).Should().Equal("NewBilling", "OldShipping");
        (await db.ActivityLogs.SingleAsync(a => a.Action == "address-added")).Description.Should().Contain("replaces OldBilling, OldBoth as default");
    }

    [Fact]
    public async Task A_new_default_both_address_clears_every_default()
    {
        using var db = TestDbContextFactory.Create();
        var customer = await SeedAsync(db,
            Address("OldBilling", AddressType.Billing, true),
            Address("OldShipping", AddressType.Shipping, true));

        await CreateHandler(db).Handle(
            new CreateCustomerAddressCommand(customer.Id, "NewBoth", "Both", "2 Elm St", null,
                "Springfield", "IL", "62701", "US", true),
            CancellationToken.None);

        (await DefaultLabelsAsync(db)).Should().Equal("NewBoth");
    }

    [Fact]
    public async Task A_new_non_default_address_leaves_defaults_alone()
    {
        using var db = TestDbContextFactory.Create();
        var customer = await SeedAsync(db, Address("OldBilling", AddressType.Billing, true));

        await CreateHandler(db).Handle(
            new CreateCustomerAddressCommand(customer.Id, "NewBilling", "Billing", "2 Elm St", null,
                "Springfield", "IL", "62701", "US", false),
            CancellationToken.None);

        (await DefaultLabelsAsync(db)).Should().Equal("OldBilling");
    }

    [Fact]
    public async Task Setting_a_shipping_address_as_default_clears_shipping_and_both_defaults_only()
    {
        using var db = TestDbContextFactory.Create();
        var target = Address("NewShipping", AddressType.Shipping, false);
        await SeedAsync(db,
            Address("OldBilling", AddressType.Billing, true),
            Address("OldShipping", AddressType.Shipping, true),
            Address("OldBoth", AddressType.Both, true),
            target);

        await new UpdateCustomerAddressHandler(new CustomerAddressRepository(db), db).Handle(
            new UpdateCustomerAddressCommand(target.Id, target.Label, "Shipping", target.Line1, null,
                target.City, target.State, target.PostalCode, target.Country, true),
            CancellationToken.None);

        (await DefaultLabelsAsync(db)).Should().Equal("NewShipping", "OldBilling");
    }

    [Fact]
    public async Task Changing_a_default_address_type_to_both_clears_the_other_defaults()
    {
        using var db = TestDbContextFactory.Create();
        var target = Address("Main", AddressType.Billing, true);
        await SeedAsync(db, target, Address("Dock", AddressType.Shipping, true));

        await new UpdateCustomerAddressHandler(new CustomerAddressRepository(db), db).Handle(
            new UpdateCustomerAddressCommand(target.Id, target.Label, "Both", target.Line1, null,
                target.City, target.State, target.PostalCode, target.Country, true),
            CancellationToken.None);

        (await DefaultLabelsAsync(db)).Should().Equal("Main");
    }
}
