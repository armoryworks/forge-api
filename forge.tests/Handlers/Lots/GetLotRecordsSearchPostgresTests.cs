using FluentAssertions;

using Forge.Api.Features.Lots;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Lots;

[Collection(PostgresCollection.Name)]
public sealed class GetLotRecordsSearchPostgresTests(PostgresFixture fixture)
{
    private async Task<(int PartId, string Tag)> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await using var seed = fixture.CreateContext();
        var part = new Part
        {
            PartNumber = $"LS-{tag}",
            Name = $"Gate fitting {tag}",
            Description = $"Brass piano hinge {tag}",
        };
        seed.Parts.Add(part);
        await seed.SaveChangesAsync();
        seed.LotRecords.Add(new LotRecord
        {
            LotNumber = $"R-26-{tag}",
            PartId = part.Id,
            Quantity = 5m,
            SupplierLotNumber = $"LARKFEN-{tag}",
        });
        await seed.SaveChangesAsync();
        return (part.Id, tag);
    }

    private async Task<List<string>> SearchAsync(int partId, string term)
    {
        await using var db = fixture.CreateContext();
        var result = await new GetLotRecordsHandler(db)
            .Handle(new GetLotRecordsQuery(partId, null, term), CancellationToken.None);
        return result.Select(r => r.LotNumber).ToList();
    }

    [Fact]
    public async Task Search_MatchesLotAndSupplierLotIgnoringCase()
    {
        var (partId, tag) = await SeedAsync();

        (await SearchAsync(partId, $"r-26-{tag.ToLowerInvariant()}")).Should().ContainSingle();
        (await SearchAsync(partId, $"larkfen-{tag.ToLowerInvariant()}")).Should().ContainSingle();
    }

    [Fact]
    public async Task Search_MatchesThePartsDescriptionAndName()
    {
        var (partId, tag) = await SeedAsync();

        (await SearchAsync(partId, "PIANO")).Should().ContainSingle();
        (await SearchAsync(partId, $"gate FITTING {tag.ToLowerInvariant()}")).Should().ContainSingle();
    }

    [Fact]
    public async Task Search_TreatsWildcardCharactersLiterally()
    {
        var (partId, _) = await SeedAsync();

        (await SearchAsync(partId, "%")).Should().BeEmpty();
        (await SearchAsync(partId, "R_26")).Should().BeEmpty();
    }
}
