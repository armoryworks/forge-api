using FluentAssertions;

using Forge.Core.Entities;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Repositories;

[Collection(PostgresCollection.Name)]
public sealed class SearchRepositoryContactTests(PostgresFixture fixture)
{
    private async Task<(Customer Customer, Contact Contact)> SeedAsync(string stem)
    {
        await using var db = fixture.CreateContext();
        var customer = new Customer { Name = $"{stem} Buyer", CompanyName = $"{stem} Industries" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var contact = new Contact
        {
            CustomerId = customer.Id,
            FirstName = "Avery",
            LastName = $"Q{stem}",
            Email = $"avery.{stem}@example.test",
            Phone = "555-0142",
        };
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();
        return (customer, contact);
    }

    [Fact]
    public async Task Contact_matches_by_email()
    {
        var stem = $"c{Guid.NewGuid():N}"[..10];
        var (customer, contact) = await SeedAsync(stem);

        await using var search = fixture.CreateContext();
        var results = await new SearchRepository(search).SearchAsync($"avery.{stem}@example.test", 20, true, CancellationToken.None);

        var hit = results.Should().ContainSingle(r => r.EntityType == "Contact").Subject;
        hit.EntityId.Should().Be(contact.Id);
        hit.Title.Should().Be($"Q{stem}, Avery");
        hit.Subtitle.Should().Contain($"{stem} Industries");
        hit.Url.Should().Be($"/customers/{customer.Id}/contacts");
    }

    [Fact]
    public async Task Contact_matches_by_full_name()
    {
        var stem = $"n{Guid.NewGuid():N}"[..10];
        var (_, contact) = await SeedAsync(stem);

        await using var search = fixture.CreateContext();
        var results = await new SearchRepository(search).SearchAsync($"Avery Q{stem}", 20, true, CancellationToken.None);

        results.Should().ContainSingle(r => r.EntityType == "Contact").Which.EntityId.Should().Be(contact.Id);
    }

    [Fact]
    public async Task Contacts_are_left_out_unless_asked_for()
    {
        var stem = $"x{Guid.NewGuid():N}"[..10];
        await SeedAsync(stem);

        await using var search = fixture.CreateContext();
        var results = await new SearchRepository(search).SearchAsync($"avery.{stem}", 20, CancellationToken.None);

        results.Should().NotContain(r => r.EntityType == "Contact");
    }
}
