using AwesomeAssertions;
using Moq;

using Forge.Api.Features.Shipping;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Shipping;

public sealed class PickListPdfDocumentTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 15, 0, 0, TimeSpan.Zero);
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    public void Dispose() => _db.Dispose();

    private static PickWave Wave(string notes = "Stage at door 2")
        => new()
        {
            WaveNumber = "WAVE-0042",
            Status = PickWaveStatus.Released,
            Strategy = PickWaveStrategy.Zone,
            TotalLines = 2,
            PickedLines = 1,
            Notes = notes,
            Lines =
            [
                new PickLine { SortOrder = 2, BinPath = "A1 / Bin 3", RequestedQuantity = 5m, PickedQuantity = 0m, Part = new Part { PartNumber = "P-2", Description = "Pin" } },
                new PickLine { SortOrder = 1, RequestedQuantity = 2.5m, PickedQuantity = 2.5m, Status = PickLineStatus.Picked, Part = new Part { PartNumber = "P-1", Description = "Bracket <b>bold</b>" }, FromLocation = new StorageLocation { Name = "Dock" } },
            ],
        };

    [Fact]
    public void Pick_list_encodes_what_users_typed()
    {
        var html = PickListPdfDocument.Build(Wave("</div><script>fetch('x')</script>"), "Acme <b>Co</b>", Now).Html;

        html.Should().NotContain("<script").And.NotContain("<b>");
        html.Should().Contain("Bracket &lt;b&gt;bold&lt;/b&gt;").And.Contain("Acme &lt;b&gt;Co&lt;/b&gt;").And.Contain("&lt;/div&gt;&lt;script&gt;");
    }

    [Fact]
    public void Pick_list_footer_carries_the_wave_number_and_rows_follow_the_sort_order()
    {
        var html = PickListPdfDocument.Build(Wave(), "Acme", Now).Html;

        html.Should().Contain("<div class=\"page-footer\">WAVE-0042 &middot; Page ");
        html.Should().Contain("PICK LIST").And.Contain("Wave #WAVE-0042").And.Contain("Status: Released")
            .And.Contain("Date: Oct 26, 2026").And.Contain("Total Items: 7.5").And.Contain("Picked By");
        html.IndexOf("P-1", StringComparison.Ordinal).Should().BeLessThan(html.IndexOf("P-2", StringComparison.Ordinal));
        html.Should().Contain("<td>Dock</td>").And.Contain("<td>A1 / Bin 3</td>");
    }

    [Fact]
    public async Task Generate_pick_list_prints_through_the_renderer()
    {
        var part = new Part { PartNumber = "P-9", Name = "Nut" };
        var location = new StorageLocation { Name = "Aisle 9", LocationType = LocationType.Area, IsActive = true };
        _db.AddRange(part, location);
        await _db.SaveChangesAsync();
        var wave = new PickWave { WaveNumber = "WAVE-0043", Lines = [new PickLine { PartId = part.Id, FromLocationId = location.Id, RequestedQuantity = 3m }] };
        _db.PickWaves.Add(wave);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var renderer = new FakeHtmlPdfRenderer();

        var pdf = await new GeneratePickListPdfHandler(_db, Mock.Of<ISystemSettingRepository>(), Mock.Of<IClock>(c => c.UtcNow == Now), renderer)
            .Handle(new GeneratePickListPdfQuery(wave.Id), CancellationToken.None);

        pdf.Should().StartWith("%PDF-"u8.ToArray());
        renderer.Documents.Should().ContainSingle().Which.Html.Should().Contain("WAVE-0043").And.Contain("P-9").And.Contain("Aisle 9");
    }
}
