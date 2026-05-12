using System.Text;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.PriceLists;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.PriceLists;

/// <summary>
/// Coverage for the PriceListEntry CSV bulk-import preview + apply handlers.
/// Two-step flow per the universal ERP convention surveyed in
/// phase-4-output/pricelist-entry-edit-ux.md.
/// </summary>
public class PriceListEntryBulkImportTests
{
    private readonly Mock<ICurrencyService> _currency = new();
    private readonly AppDbContext _db;
    private readonly IPriceListRepository _repo;
    private readonly int _priceListId;
    private readonly int _partAId;
    private readonly int _partBId;

    public PriceListEntryBulkImportTests()
    {
        _db = TestDbContextFactory.Create();
        _repo = new PriceListRepository(_db);

        // Seed: one price list + two parts. Parts get unique numbers so the
        // case-insensitive lookup test exercises the same path real data does.
        var pl = new PriceList { Name = "Standard", IsDefault = true, IsActive = true };
        _db.PriceLists.Add(pl);

        var partA = new Part { PartNumber = "PART-001", Name = "Widget A" };
        var partB = new Part { PartNumber = "PART-002", Name = "Widget B" };
        _db.Parts.Add(partA);
        _db.Parts.Add(partB);
        _db.SaveChanges();

        _priceListId = pl.Id;
        _partAId = partA.Id;
        _partBId = partB.Id;

        _currency.Setup(c => c.GetBaseCurrencyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("USD");
    }

    private static IFormFile MakeCsv(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", "import.csv")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/csv",
        };
    }

    [Fact]
    public async Task Preview_ValidRows_ReturnsAddActionForAll()
    {
        var csv = "partNumber,unitPrice,minQuantity\nPART-001,5.00,1\nPART-002,10.00,1\n";
        var handler = new PreviewPriceListEntryImportHandler(_db, _repo, _currency.Object);

        var result = await handler.Handle(
            new PreviewPriceListEntryImportCommand(_priceListId, MakeCsv(csv)),
            CancellationToken.None);

        result.TotalRows.Should().Be(2);
        result.AddCount.Should().Be(2);
        result.UpdateCount.Should().Be(0);
        result.ErrorCount.Should().Be(0);
        result.Rows.Should().AllSatisfy(r => r.Action.Should().Be(BulkImportRowAction.Add));
    }

    [Fact]
    public async Task Preview_UnknownPartNumber_ReturnsErrorRow()
    {
        var csv = "partNumber,unitPrice\nPART-DOES-NOT-EXIST,5.00\n";
        var handler = new PreviewPriceListEntryImportHandler(_db, _repo, _currency.Object);

        var result = await handler.Handle(
            new PreviewPriceListEntryImportCommand(_priceListId, MakeCsv(csv)),
            CancellationToken.None);

        result.ErrorCount.Should().Be(1);
        result.Rows[0].Action.Should().Be(BulkImportRowAction.Error);
        result.Rows[0].ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task Preview_ExistingEntryAtSameTier_ReturnsUpdateAction()
    {
        // Pre-seed an entry at (PART-001, minQty=1) so the next preview
        // classifies the same key as Update.
        _db.PriceListEntries.Add(new PriceListEntry
        {
            PriceListId = _priceListId,
            PartId = _partAId,
            UnitPrice = 4.00m,
            MinQuantity = 1,
            Currency = "USD",
        });
        await _db.SaveChangesAsync();

        var csv = "partNumber,unitPrice,minQuantity\nPART-001,7.50,1\n";
        var handler = new PreviewPriceListEntryImportHandler(_db, _repo, _currency.Object);

        var result = await handler.Handle(
            new PreviewPriceListEntryImportCommand(_priceListId, MakeCsv(csv)),
            CancellationToken.None);

        result.UpdateCount.Should().Be(1);
        result.Rows[0].Action.Should().Be(BulkImportRowAction.Update);
        result.Rows[0].PartId.Should().Be(_partAId);
    }

    [Fact]
    public async Task Preview_MalformedUnitPrice_ReturnsError()
    {
        var csv = "partNumber,unitPrice\nPART-001,not-a-number\n";
        var handler = new PreviewPriceListEntryImportHandler(_db, _repo, _currency.Object);

        var result = await handler.Handle(
            new PreviewPriceListEntryImportCommand(_priceListId, MakeCsv(csv)),
            CancellationToken.None);

        result.ErrorCount.Should().Be(1);
        result.Rows[0].Action.Should().Be(BulkImportRowAction.Error);
        result.Rows[0].ErrorMessage.Should().Contain("unitPrice");
    }

    [Fact]
    public async Task Apply_AddRows_PersistsEntries()
    {
        var csv = "partNumber,unitPrice,minQuantity,notes\nPART-001,5.00,1,intro tier\nPART-002,10.00,1,\n";
        var handler = new ApplyPriceListEntryImportHandler(_db, _repo, _currency.Object);

        var result = await handler.Handle(
            new ApplyPriceListEntryImportCommand(_priceListId, MakeCsv(csv)),
            CancellationToken.None);

        result.AddedCount.Should().Be(2);
        result.UpdatedCount.Should().Be(0);
        result.ErrorCount.Should().Be(0);

        var saved = await _db.PriceListEntries
            .Where(e => e.PriceListId == _priceListId)
            .ToListAsync();
        saved.Should().HaveCount(2);
        saved.Should().Contain(e => e.PartId == _partAId && e.UnitPrice == 5.00m && e.Notes == "intro tier");
        saved.Should().Contain(e => e.PartId == _partBId && e.UnitPrice == 10.00m);
    }

    [Fact]
    public async Task Apply_RerunSameCsv_IsIdempotent()
    {
        var csv = "partNumber,unitPrice\nPART-001,5.00\nPART-002,10.00\n";
        var handler = new ApplyPriceListEntryImportHandler(_db, _repo, _currency.Object);

        // First apply — adds.
        var first = await handler.Handle(
            new ApplyPriceListEntryImportCommand(_priceListId, MakeCsv(csv)),
            CancellationToken.None);
        first.AddedCount.Should().Be(2);

        // Second apply — same data, should classify as updates not duplicates.
        var second = await handler.Handle(
            new ApplyPriceListEntryImportCommand(_priceListId, MakeCsv(csv)),
            CancellationToken.None);
        second.AddedCount.Should().Be(0);
        second.UpdatedCount.Should().Be(2);

        var saved = await _db.PriceListEntries
            .Where(e => e.PriceListId == _priceListId)
            .ToListAsync();
        saved.Should().HaveCount(2);
    }
}
