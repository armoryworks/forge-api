using FluentAssertions;

using Forge.Core.Entities;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Repositories;

[Collection(PostgresCollection.Name)]
public sealed class SearchRepositoryRankingTests(PostgresFixture fixture)
{
    private static string Stem() => $"R{Guid.NewGuid():N}"[..12].ToUpperInvariant();

    [Fact]
    public async Task Exact_match_comes_before_longer_and_more_recently_updated_matches()
    {
        var stem = Stem();
        await using (var db = fixture.CreateContext())
        {
            db.Parts.Add(new Part { PartNumber = $"{stem}-1", Name = "Exact" });
            await db.SaveChangesAsync();
            db.Parts.Add(new Part { PartNumber = $"{stem}-10", Name = "Ten" });
            db.Parts.Add(new Part { PartNumber = $"{stem}-15", Name = "Fifteen" });
            await db.SaveChangesAsync();
        }

        await using var search = fixture.CreateContext();
        var results = await new SearchRepository(search).SearchAsync($"{stem}-1", 20, CancellationToken.None);

        results.Select(r => r.Title).Should().StartWith(new[] { $"{stem}-1" });
        results.Select(r => r.Title).Should().Contain(new[] { $"{stem}-10", $"{stem}-15" });
    }

    [Fact]
    public async Task Prefix_match_on_one_entity_outranks_a_contains_match_on_another()
    {
        var stem = Stem();
        await using (var db = fixture.CreateContext())
        {
            db.Customers.Add(new Customer { Name = $"Acme {stem}" });
            db.Parts.Add(new Part { PartNumber = $"{stem}-BRK", Name = "Bracket" });
            await db.SaveChangesAsync();
        }

        await using var search = fixture.CreateContext();
        var results = await new SearchRepository(search).SearchAsync(stem, 20, CancellationToken.None);

        results.Select(r => r.EntityType).Should().StartWith(new[] { "Part", "Customer" });
    }

    [Fact]
    public async Task Underscore_and_percent_in_the_term_match_literally()
    {
        var stem = Stem();
        await using (var db = fixture.CreateContext())
        {
            db.Customers.Add(new Customer { Name = $"{stem}_A" });
            db.Customers.Add(new Customer { Name = $"{stem}xA" });
            db.Customers.Add(new Customer { Name = $"{stem}%B" });
            db.Customers.Add(new Customer { Name = $"{stem}yyB" });
            await db.SaveChangesAsync();
        }

        await using var search = fixture.CreateContext();
        var repo = new SearchRepository(search);

        (await repo.SearchAsync($"{stem}_", 20, CancellationToken.None))
            .Select(r => r.Title).Should().BeEquivalentTo(new[] { $"{stem}_A" });
        (await repo.SearchAsync($"{stem}%", 20, CancellationToken.None))
            .Select(r => r.Title).Should().BeEquivalentTo(new[] { $"{stem}%B" });
    }
}
