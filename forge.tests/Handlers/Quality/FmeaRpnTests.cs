using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Quality;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Quality;

public class FmeaRpnTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly IClock _clock = Mock.Of<IClock>(c => c.UtcNow == Now);

    public void Dispose() => _db.Dispose();

    private async Task<FmeaItem> ItemAsync()
    {
        var fmea = new FmeaAnalysis { FmeaNumber = "FMEA-001", Name = "Weld cell", Type = FmeaType.Process };
        fmea.Items.Add(new FmeaItem
        {
            ItemNumber = 1, FailureMode = "Porosity", PotentialEffect = "Leak",
            Severity = 7, Occurrence = 6, Detection = 5,
        });
        fmea.Items.Add(new FmeaItem
        {
            ItemNumber = 2, FailureMode = "Spatter", PotentialEffect = "Cosmetic",
            Severity = 2, Occurrence = 3, Detection = 2,
        });
        _db.Set<FmeaAnalysis>().Add(fmea);
        await _db.SaveChangesAsync();
        return fmea.Items.Single(i => i.ItemNumber == 1);
    }

    [Fact]
    public async Task RecordAction_WithOnlyARevisedOccurrence_CarriesTheOtherRatingsOver()
    {
        var item = await ItemAsync();

        var result = await new RecordFmeaActionHandler(_db, _clock).Handle(
            new RecordFmeaActionCommand(item.FmeaId, item.Id,
                new RecordFmeaActionRequestModel { ActionTaken = "Added pre-heat", RevisedOccurrence = 3 }),
            CancellationToken.None);

        result.Rpn.Should().Be(210);
        result.RevisedRpn.Should().Be(105);
        result.RevisedSeverity.Should().BeNull();
        result.RevisedDetection.Should().BeNull();
    }

    [Fact]
    public async Task FmeaFigures_UseTheRevisedRpn_OnceAnActionIsRecorded()
    {
        var item = await ItemAsync();
        var highBefore = await new GetHighRpnItemsHandler(_db).Handle(new GetHighRpnItemsQuery(), CancellationToken.None);
        highBefore.Select(i => i.Id).Should().Equal(item.Id);

        await new RecordFmeaActionHandler(_db, _clock).Handle(
            new RecordFmeaActionCommand(item.FmeaId, item.Id,
                new RecordFmeaActionRequestModel { ActionTaken = "Added pre-heat", RevisedOccurrence = 3 }),
            CancellationToken.None);

        var fmea = await new GetFmeaHandler(_db).Handle(new GetFmeaQuery(item.FmeaId), CancellationToken.None);
        fmea.MaxRpn.Should().Be(105);
        fmea.HighRpnCount.Should().Be(0);
        fmea.Items.Single(i => i.Id == item.Id).RevisedRpn.Should().Be(105);
        fmea.Items.Single(i => i.ItemNumber == 2).RevisedRpn.Should().BeNull();

        var list = await new GetFmeasHandler(_db).Handle(new GetFmeasQuery(null, null, null), CancellationToken.None);
        list.Single().MaxRpn.Should().Be(105);

        var summary = await new GetFmeaRiskSummaryHandler(_db).Handle(
            new GetFmeaRiskSummaryQuery(item.FmeaId), CancellationToken.None);
        summary.MaxRpn.Should().Be(105);
        summary.HighRpnItems.Should().Be(0);
        summary.HeatmapData.Should().Contain(c => c.Severity == 7 && c.Occurrence == 3 && c.Detection == 5);

        (await new GetHighRpnItemsHandler(_db).Handle(new GetHighRpnItemsQuery(), CancellationToken.None))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task HighRpnItems_NameTheFmeaTheyBelongTo()
    {
        var item = await ItemAsync();

        var high = await new GetHighRpnItemsHandler(_db).Handle(new GetHighRpnItemsQuery(), CancellationToken.None);

        var row = high.Should().ContainSingle().Subject;
        row.FmeaId.Should().Be(item.FmeaId);
        row.FmeaNumber.Should().Be("FMEA-001");
        row.FmeaName.Should().Be("Weld cell");
    }
}
