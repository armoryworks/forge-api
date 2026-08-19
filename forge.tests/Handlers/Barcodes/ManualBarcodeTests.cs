using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Barcodes;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Barcodes;

/// <summary>
/// Manual alternate barcodes: a user can register extra scannable values (manufacturer UPC, vendor
/// SKU, legacy label) on top of an entity's auto-assigned system code. They coexist, resolve on scan,
/// are the only ones removable, and survive a system-code re-sync.
/// </summary>
public class ManualBarcodeTests
{
    private const int PartId = 1;

    private static AppDbContext SeededDb()
    {
        var db = TestDbContextFactory.Create();
        db.Parts.Add(new Part { Id = PartId, PartNumber = "P-001", Name = "Bracket", Description = "Bracket" });
        db.Barcodes.Add(new Barcode
        {
            Id = 1,
            Value = "PRT-P-001",
            EntityType = BarcodeEntityType.Part,
            PartId = PartId,
            IsActive = true,
            IdentityType = BarcodeIdentityType.Internal,
            Source = BarcodeSource.System,
        });
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task AddManualBarcode_coexists_with_the_system_code_and_resolves_on_scan()
    {
        await using var db = SeededDb();

        var result = await new AddManualBarcodeHandler(db)
            .Handle(new AddManualBarcodeCommand(BarcodeEntityType.Part, PartId, " 049000042566 "), default);

        result.Value.Should().Be("049000042566"); // trimmed
        result.Source.Should().Be("Manual");

        db.Barcodes.Count(b => b.PartId == PartId).Should().Be(2); // system + manual, side by side
        var resolved = await new BarcodeService(db, new HttpContextAccessor())
            .FindByValueAsync("049000042566");
        resolved!.PartId.Should().Be(PartId); // a scan of the alias maps to the part
    }

    [Fact]
    public async Task AddManualBarcode_rejects_a_value_already_in_use()
    {
        await using var db = SeededDb();
        var act = () => new AddManualBarcodeHandler(db)
            .Handle(new AddManualBarcodeCommand(BarcodeEntityType.Part, PartId, "PRT-P-001"), default);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("already in use");
    }

    [Fact]
    public async Task AddManualBarcode_rejects_an_empty_value()
    {
        await using var db = SeededDb();
        var act = () => new AddManualBarcodeHandler(db)
            .Handle(new AddManualBarcodeCommand(BarcodeEntityType.Part, PartId, "   "), default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AddManualBarcode_rejects_a_missing_entity()
    {
        await using var db = SeededDb();
        var act = () => new AddManualBarcodeHandler(db)
            .Handle(new AddManualBarcodeCommand(BarcodeEntityType.Part, 999, "X-1"), default);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task RemoveManualBarcode_removes_the_alias_but_refuses_the_system_code()
    {
        await using var db = SeededDb();
        var manual = await new AddManualBarcodeHandler(db)
            .Handle(new AddManualBarcodeCommand(BarcodeEntityType.Part, PartId, "ALT-1"), default);

        // The system code cannot be removed.
        var refuse = () => new RemoveManualBarcodeHandler(db).Handle(new RemoveManualBarcodeCommand(1), default);
        await refuse.Should().ThrowAsync<InvalidOperationException>();

        // The manual alias can.
        await new RemoveManualBarcodeHandler(db).Handle(new RemoveManualBarcodeCommand(manual.Id), default);
        db.Barcodes.Any(b => b.Id == manual.Id).Should().BeFalse();
        db.Barcodes.Count(b => b.PartId == PartId).Should().Be(1); // only the system code remains
    }

    [Fact]
    public async Task RefreshPartBarcode_leaves_manual_aliases_untouched()
    {
        await using var db = SeededDb();
        await new AddManualBarcodeHandler(db)
            .Handle(new AddManualBarcodeCommand(BarcodeEntityType.Part, PartId, "ALT-1"), default);

        // Assign a GTIN and re-sync the system code — the manual alias must survive.
        var part = await db.Parts.FirstAsync(p => p.Id == PartId);
        part.Gtin = "0614141000012";
        await db.SaveChangesAsync();
        await new BarcodeService(db, new HttpContextAccessor()).RefreshPartBarcodeAsync(PartId);

        var codes = db.Barcodes.Where(b => b.PartId == PartId).ToList();
        codes.Should().Contain(b => b.Source == BarcodeSource.Manual && b.Value == "ALT-1");
        codes.Should().Contain(b => b.Source == BarcodeSource.System && b.Value == "0614141000012");
    }
}
