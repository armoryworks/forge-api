using FluentAssertions;
using Moq;

using Forge.Api.Features.Parts.BulkIntake;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Parts;

/// <summary>Part bulk-intake: classification (preview) + persistence (commit), dedup against the
/// batch and existing parts, lenient enum parsing, and per-batch sequential part numbering.</summary>
public class BulkPartIntakeHandlerTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IPartRepository> _repo = new();
    private readonly Mock<IBarcodeService> _barcodes = new();
    private readonly BulkPartIntakeHandler _handler;

    public BulkPartIntakeHandlerTests()
    {
        // One seed per class; the handler increments locally from here.
        _repo.Setup(r => r.GetNextPartNumberAsync(It.IsAny<InventoryClass>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InventoryClass ic, CancellationToken _) => ic switch
            {
                InventoryClass.Subassembly => "ASM-00001",
                InventoryClass.Raw => "RAW-00001",
                _ => "PRT-00001",
            });
        _barcodes.Setup(b => b.CreateBarcodeAsync(It.IsAny<BarcodeEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Barcode());
        _handler = new BulkPartIntakeHandler(_db, _repo.Object, _barcodes.Object);
    }

    private static BulkPartIntakeRow Row(string? name, string? proc = null, string? inv = null, string? ext = null, string? key = null)
        => new(key ?? name, name!, null, proc, inv, ext);

    private async Task<BulkPartIntakeResponseModel> Preview(params BulkPartIntakeRow[] rows)
        => await _handler.Handle(new BulkPartIntakeCommand(new BulkPartIntakeRequest(rows.ToList()), Commit: false), CancellationToken.None);

    private async Task<BulkPartIntakeResponseModel> Commit(params BulkPartIntakeRow[] rows)
        => await _handler.Handle(new BulkPartIntakeCommand(new BulkPartIntakeRequest(rows.ToList()), Commit: true), CancellationToken.None);

    [Fact]
    public async Task Empty_returns_zero_summary()
    {
        var result = await Preview();
        result.Should().BeEquivalentTo(new BulkPartIntakeResponseModel(0, 0, 0, []));
    }

    [Fact]
    public async Task Preview_classifies_new_invalid_and_within_batch_duplicate_without_persisting()
    {
        var result = await Preview(
            Row("Bracket", key: "r1"),
            Row("  ", key: "r2"),          // no name → Invalid
            Row("Bracket", key: "r3"));    // dup of r1 within batch

        result.TotalRows.Should().Be(3);
        result.CreatedCount.Should().Be(1);
        result.SkippedCount.Should().Be(2);
        result.Results.Single(r => r.ExternalRowKey == "r1").Status.Should().Be(BulkPartIntakeRowStatus.Created);
        result.Results.Single(r => r.ExternalRowKey == "r2").Status.Should().Be(BulkPartIntakeRowStatus.Invalid);
        result.Results.Single(r => r.ExternalRowKey == "r3").Status.Should().Be(BulkPartIntakeRowStatus.DuplicateWithinBatch);

        // Dry run: nothing saved, no barcodes issued.
        _db.Parts.Should().BeEmpty();
        _barcodes.Verify(b => b.CreateBarcodeAsync(It.IsAny<BarcodeEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Commit_persists_new_parts_with_sequential_numbers_and_barcodes()
    {
        var result = await Commit(
            Row("Alpha"),
            Row("Bravo"),
            Row("Charlie"));

        result.CreatedCount.Should().Be(3);
        _db.Parts.Should().HaveCount(3);

        // Seeded once at PRT-00001, incremented locally for the same class.
        var created = result.Results.Where(r => r.Status == BulkPartIntakeRowStatus.Created).ToList();
        created.Select(r => r.CreatedPartNumber).Should().BeEquivalentTo(new[] { "PRT-00001", "PRT-00002", "PRT-00003" });
        created.Should().OnlyContain(r => r.CreatedPartId != null);

        _barcodes.Verify(b => b.CreateBarcodeAsync(BarcodeEntityType.Part, It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Commit_numbers_each_inventory_class_from_its_own_prefix()
    {
        var result = await Commit(
            Row("Widget", inv: "component"),
            Row("Frame", inv: "subassembly"),
            Row("Steel", inv: "raw material"));

        var byName = result.Results.ToDictionary(r => r.ExternalRowKey!, r => r.CreatedPartNumber);
        byName["Widget"].Should().Be("PRT-00001");
        byName["Frame"].Should().Be("ASM-00001");
        byName["Steel"].Should().Be("RAW-00001");
    }

    [Fact]
    public async Task Parses_procurement_and_inventory_leniently_with_fallbacks()
    {
        await Commit(
            Row("Purchased item", proc: "purchase", inv: "finished good"),
            Row("Gibberish axes", proc: "???", inv: "???"));

        var purchased = _db.Parts.Single(p => p.Name == "Purchased item");
        purchased.ProcurementSource.Should().Be(ProcurementSource.Buy);
        purchased.InventoryClass.Should().Be(InventoryClass.FinishedGood);

        var fallback = _db.Parts.Single(p => p.Name == "Gibberish axes");
        fallback.ProcurementSource.Should().Be(ProcurementSource.Buy);
        fallback.InventoryClass.Should().Be(InventoryClass.Component);
    }

    [Fact]
    public async Task Skips_rows_matching_an_existing_part_by_name_or_external_id()
    {
        _db.Parts.Add(new Part { PartNumber = "PRT-09000", Name = "Existing Widget", Revision = "A", ExternalId = "LEGACY-1" });
        await _db.SaveChangesAsync();

        var result = await Commit(
            Row("Existing Widget", key: "byName"),
            Row("New By Ext", ext: "legacy-1", key: "byExt"),   // case-insensitive external-id match
            Row("Genuinely New", key: "new"));

        result.Results.Single(r => r.ExternalRowKey == "byName").Status.Should().Be(BulkPartIntakeRowStatus.DuplicateExistingPart);
        result.Results.Single(r => r.ExternalRowKey == "byExt").Status.Should().Be(BulkPartIntakeRowStatus.DuplicateExistingPart);
        result.Results.Single(r => r.ExternalRowKey == "new").Status.Should().Be(BulkPartIntakeRowStatus.Created);

        // Only the one genuinely-new part was added on top of the seeded one.
        _db.Parts.Should().HaveCount(2);
    }

    [Fact]
    public async Task Over_cap_throws()
    {
        var rows = Enumerable.Range(0, 1001).Select(i => Row($"Part {i}")).ToArray();
        var act = () => Commit(rows);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
