using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Data;
using Forge.Api.Data.TrainingContent;
using Forge.Tests.Helpers;

namespace Forge.Tests.Seed;

/// <summary>
/// Seeds the real training content into Postgres. The shape tests run against
/// the in-memory provider, which enforces neither the column caps nor jsonb
/// validity — so a long summary or malformed content would otherwise surface
/// as a crash while seeding a fresh install. Also proves the seeders are
/// re-runnable, which is what happens on every API boot.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TrainingSeedPostgresTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Training_content_seeds_into_postgres_and_reseeds_cleanly()
    {
        await using var db = fixture.CreateContext();

        var slugMap = await TrainingContentBase.LoadSlugMapAsync(db);
        foreach (var seeder in SeedData.CreateSeeders(db, slugMap))
            await seeder.SeedAsync();

        var afterFirst = await db.TrainingModules.CountAsync();
        afterFirst.Should().BeGreaterThan(0);

        // Re-seed: GetOrCreateModule updates in place, so the count must hold.
        var secondMap = await TrainingContentBase.LoadSlugMapAsync(db);
        foreach (var seeder in SeedData.CreateSeeders(db, secondMap))
            await seeder.SeedAsync();

        (await db.TrainingModules.CountAsync()).Should().Be(afterFirst);

        // Every module survived the round trip with its content intact.
        var modules = await db.TrainingModules.AsNoTracking().ToListAsync();
        modules.Should().OnlyContain(m => m.ContentJson.Length > 2);
        modules.Select(m => m.Slug).Should().OnlyHaveUniqueItems();
    }
}
