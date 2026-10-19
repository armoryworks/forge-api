using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Repositories;

[Collection(PostgresCollection.Name)]
public sealed class SearchRepositoryCustomerSubtitleTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Customer_whose_company_name_matches_its_name_has_no_subtitle()
    {
        var name = $"Subtitle {Guid.NewGuid():N}"[..24];
        await using (var db = fixture.CreateContext())
        {
            db.Customers.Add(new Customer { Name = name, CompanyName = name });
            await db.SaveChangesAsync();
        }

        await using var search = fixture.CreateContext();
        var results = await new SearchRepository(search).SearchAsync(name, 20, CancellationToken.None);

        results.Should().ContainSingle(r => r.EntityType == "Customer" && r.Title == name)
            .Which.Subtitle.Should().BeNull();
    }

    [Fact]
    public async Task Customer_with_a_different_company_name_keeps_it_as_the_subtitle()
    {
        var name = $"Contact {Guid.NewGuid():N}"[..24];
        var company = $"{name} Holdings";
        await using (var db = fixture.CreateContext())
        {
            db.Customers.Add(new Customer { Name = name, CompanyName = company });
            await db.SaveChangesAsync();
        }

        await using var search = fixture.CreateContext();
        var results = await new SearchRepository(search).SearchAsync(name, 20, CancellationToken.None);

        results.Should().ContainSingle(r => r.EntityType == "Customer" && r.Title == name)
            .Which.Subtitle.Should().Be(company);
    }

    [Fact]
    public async Task Search_leaves_the_context_usable_for_later_queries()
    {
        await using var db = fixture.CreateContext();

        await new SearchRepository(db).SearchAsync("anything", 5, CancellationToken.None);
        var act = () => db.Customers.CountAsync();

        await act.Should().NotThrowAsync();
    }
}
