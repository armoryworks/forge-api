using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Customers;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Customers;

public class ContactPrimaryTests
{
    private static async Task<(Customer Customer, Contact Primary, Contact Other)> SeedAsync(AppDbContext db)
    {
        var customer = new Customer { Name = "Acme" };
        var primary = new Contact { FirstName = "Ada", LastName = "Byron", IsPrimary = true };
        var other = new Contact { FirstName = "Bob", LastName = "Cole" };
        customer.Contacts.Add(primary);
        customer.Contacts.Add(other);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return (customer, primary, other);
    }

    [Fact]
    public async Task Creating_a_primary_contact_unmarks_the_previous_primary()
    {
        using var db = TestDbContextFactory.Create();
        var (customer, primary, _) = await SeedAsync(db);

        var result = await new CreateContactHandler(new CustomerRepository(db), db).Handle(
            new CreateContactCommand(customer.Id, "Cy", "Dunn", null, null, null, true, Fax: "555-0100"),
            CancellationToken.None);

        result.IsPrimary.Should().BeTrue();
        result.Fax.Should().Be("555-0100");
        var primaries = await db.Contacts.Where(c => c.IsPrimary).Select(c => c.Id).ToListAsync();
        primaries.Should().Equal(result.Id);
        (await db.Contacts.SingleAsync(c => c.Id == primary.Id)).IsPrimary.Should().BeFalse();
        (await db.ActivityLogs.AnyAsync(a => a.EntityType == "Contact" && a.EntityId == primary.Id && a.Description.Contains("cleared-primary")))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Creating_a_non_primary_contact_leaves_the_primary_alone()
    {
        using var db = TestDbContextFactory.Create();
        var (customer, primary, _) = await SeedAsync(db);

        await new CreateContactHandler(new CustomerRepository(db), db).Handle(
            new CreateContactCommand(customer.Id, "Cy", "Dunn", null, null, null, false),
            CancellationToken.None);

        (await db.Contacts.SingleAsync(c => c.IsPrimary)).Id.Should().Be(primary.Id);
    }

    [Fact]
    public async Task Marking_a_second_contact_primary_unmarks_the_first()
    {
        using var db = TestDbContextFactory.Create();
        var (customer, primary, other) = await SeedAsync(db);

        await new UpdateContactHandler(db).Handle(
            new UpdateContactCommand(customer.Id, other.Id, null, null, null, null, null, true),
            CancellationToken.None);

        (await db.Contacts.SingleAsync(c => c.IsPrimary)).Id.Should().Be(other.Id);
        (await db.Contacts.SingleAsync(c => c.Id == primary.Id)).IsPrimary.Should().BeFalse();
    }

    [Fact]
    public async Task Primary_contacts_on_other_customers_are_untouched()
    {
        using var db = TestDbContextFactory.Create();
        var (customer, _, other) = await SeedAsync(db);
        var elsewhere = new Customer { Name = "Elsewhere" };
        var elsewherePrimary = new Contact { FirstName = "Eve", LastName = "Fox", IsPrimary = true };
        elsewhere.Contacts.Add(elsewherePrimary);
        db.Customers.Add(elsewhere);
        await db.SaveChangesAsync();

        await new UpdateContactHandler(db).Handle(
            new UpdateContactCommand(customer.Id, other.Id, null, null, null, null, null, true),
            CancellationToken.None);

        (await db.Contacts.SingleAsync(c => c.Id == elsewherePrimary.Id)).IsPrimary.Should().BeTrue();
    }

    [Fact]
    public async Task Update_saves_the_contact_fax()
    {
        using var db = TestDbContextFactory.Create();
        var (customer, _, other) = await SeedAsync(db);

        var result = await new UpdateContactHandler(db).Handle(
            new UpdateContactCommand(customer.Id, other.Id, null, null, null, null, null, null, Fax: "555-0100"),
            CancellationToken.None);

        result.Fax.Should().Be("555-0100");
        (await db.Contacts.SingleAsync(c => c.Id == other.Id)).Fax.Should().Be("555-0100");
    }
}
