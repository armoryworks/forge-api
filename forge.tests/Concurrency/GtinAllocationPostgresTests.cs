using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Barcodes;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Concurrency;

/// <summary>
/// Parallel GTIN auto-allocations each read the shared next-reference counter. They serialize on an
/// advisory lock, so every request gets its own GTIN instead of all but one failing on the unique index.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GtinAllocationPostgresTests(PostgresFixture fixture)
{
    [Fact]
    public async Task FiveParallelAllocations_GetFiveDistinctGtins()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var partIds = new List<int>();
        await using (var setup = fixture.CreateContext())
        {
            var settings = new SystemSettingRepository(setup);
            await settings.UpsertAsync(Gs1.CompanyPrefixKey, "0614141", null, CancellationToken.None);
            var parts = Enumerable.Range(1, 5)
                .Select(i => new Part { PartNumber = $"GTIN-{tag}-{i}", Name = "Parallel GTIN" })
                .ToList();
            setup.Parts.AddRange(parts);
            await setup.SaveChangesAsync();
            partIds.AddRange(parts.Select(p => p.Id));
        }

        var results = await Task.WhenAll(partIds.Select(async id =>
        {
            await using var db = fixture.CreateContext();
            var handler = new AssignPartGtinHandler(db, new SystemSettingRepository(db), Mock.Of<IBarcodeService>());
            return await handler.Handle(new AssignPartGtinCommand(id, null), CancellationToken.None);
        }));

        results.Select(r => r.Gtin).Should().OnlyHaveUniqueItems().And.HaveCount(5);
        results.Should().OnlyContain(r => Gs1.IsValidGtin(r.Gtin) && r.Source == "Allocated");
        await using var verify = fixture.CreateContext();
        var stored = await verify.Parts.Where(p => partIds.Contains(p.Id)).Select(p => p.Gtin).ToListAsync();
        stored.Should().BeEquivalentTo(results.Select(r => r.Gtin));
    }
}
