using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

public class BarcodeServiceTests
{
    private BarcodeService CreateService(out Forge.Data.Context.AppDbContext db)
    {
        db = TestDbContextFactory.Create();
        var httpCtx = new Mock<IHttpContextAccessor>();
        httpCtx.Setup(x => x.HttpContext).Returns((HttpContext?)null);
        return new BarcodeService(db, httpCtx.Object);
    }

    [Fact]
    public async Task Lot_barcode_value_is_the_lot_number_without_prefix_stutter()
    {
        var service = CreateService(out var db);
        using (db)
        {
            var barcode = await service.CreateBarcodeAsync(
                BarcodeEntityType.Lot, 7, "LOT-20260707-001");

            barcode.Value.Should().Be("LOT-20260707-001",
                "lot numbers already carry the LOT- prefix — LOT-LOT-… labels are unusable");
            barcode.LotRecordId.Should().Be(7);
            barcode.EntityType.Should().Be(BarcodeEntityType.Lot);
        }
    }

    [Fact]
    public async Task Unprefixed_identifier_still_gains_the_type_prefix()
    {
        var service = CreateService(out var db);
        using (db)
        {
            var barcode = await service.CreateBarcodeAsync(
                BarcodeEntityType.Job, 3, "J-1042");

            barcode.Value.Should().Be("JOB-J-1042");
            barcode.JobId.Should().Be(3);
        }
    }

    [Fact]
    public async Task Collision_appends_entity_id()
    {
        var service = CreateService(out var db);
        using (db)
        {
            db.Barcodes.Add(new Barcode { Value = "LOT-DUP-1", EntityType = BarcodeEntityType.Lot, IsActive = true });
            await db.SaveChangesAsync();

            var barcode = await service.CreateBarcodeAsync(BarcodeEntityType.Lot, 9, "LOT-DUP-1");

            barcode.Value.Should().Be("LOT-DUP-1-9");
        }
    }
}
