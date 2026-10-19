using FluentAssertions;
using MediatR;
using Moq;

using Forge.Api.Features.Mobile;
using Forge.Api.Features.Search;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Mobile;

[Collection(PostgresCollection.Name)]
public sealed class LookupHandlerTests(PostgresFixture fixture)
{
    private readonly Mock<IMediator> _mediator = new();

    private static string Unique() => Random.Shared.Next(1_000_000, 9_999_999).ToString();

    private async Task<List<ScanResolveResponseModel>> LookupAsync(string term)
    {
        _mediator.Setup(m => m.Send(It.IsAny<GlobalSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        await using var db = fixture.CreateContext();
        var handler = new LookupHandler(_mediator.Object, db);
        return await handler.Handle(new LookupQuery(term), CancellationToken.None);
    }

    private async Task<(int Active, int Inactive)> SeedBinsAsync(string activeName, string inactiveName, string? barcode = null)
    {
        await using var db = fixture.CreateContext();
        var active = new StorageLocation { Name = activeName, LocationType = LocationType.Bin, Barcode = barcode, IsActive = true };
        var inactive = new StorageLocation { Name = inactiveName, LocationType = LocationType.Bin, IsActive = false };
        db.StorageLocations.AddRange(active, inactive);
        await db.SaveChangesAsync();
        return (active.Id, inactive.Id);
    }

    [Fact]
    public async Task Bin_name_finds_the_active_bin()
    {
        var n = Unique();
        var (active, _) = await SeedBinsAsync($"RC-{n}", $"RX-{n}");

        var results = await LookupAsync($"rc-{n}");

        results.Should().ContainSingle(r => r.Kind == "bin")
            .Which.Id.Should().Be(active);
    }

    [Fact]
    public async Task Inactive_bins_are_left_out()
    {
        var n = Unique();
        var (_, inactive) = await SeedBinsAsync($"AA-{n}", $"ZZ-{n}");

        var results = await LookupAsync($"ZZ-{n}");

        results.Should().NotContain(r => r.Id == inactive && r.Kind == "bin");
    }

    [Fact]
    public async Task Bin_barcode_finds_the_bin()
    {
        var n = Unique();
        var (active, _) = await SeedBinsAsync($"Shelf {n}", $"Old {n}", barcode: $"BC-{n}");

        var results = await LookupAsync($"BC-{n}");

        results.Should().ContainSingle(r => r.Kind == "bin")
            .Which.Id.Should().Be(active);
    }

    [Fact]
    public async Task Like_wildcards_in_the_term_are_literal()
    {
        var n = Unique();
        await SeedBinsAsync($"RC-{n}", $"RX-{n}");

        var results = await LookupAsync($"R%-{n}");

        results.Should().NotContain(r => r.Kind == "bin");
    }

    [Fact]
    public async Task Lot_number_finds_the_lot()
    {
        var number = $"LOT-Lk{Unique()}";
        int lotId;
        await using (var db = fixture.CreateContext())
        {
            var part = new Part { PartNumber = $"LKP-{Guid.NewGuid():N}"[..16], Name = "Lookup part" };
            db.Parts.Add(part);
            await db.SaveChangesAsync();
            var lot = new LotRecord { LotNumber = number, PartId = part.Id, Quantity = 1 };
            db.LotRecords.Add(lot);
            await db.SaveChangesAsync();
            lotId = lot.Id;
        }

        var results = await LookupAsync(number.ToLowerInvariant());

        results.Should().ContainSingle(r => r.Kind == "lot")
            .Which.Id.Should().Be(lotId);
    }

    [Fact]
    public async Task Global_search_jobs_still_come_through()
    {
        var term = $"QJ{Unique()}";
        _mediator.Setup(m => m.Send(It.IsAny<GlobalSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SearchResultModel("Job", 42, term, "Customer", "work", "/jobs/42")]);
        await using var db = fixture.CreateContext();

        var results = await new LookupHandler(_mediator.Object, db).Handle(new LookupQuery(term), CancellationToken.None);

        results.Should().ContainSingle(r => r.Kind == "job").Which.Id.Should().Be(42);
    }
}
