using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Mobile;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Mobile;

[Collection(PostgresCollection.Name)]
public sealed class ResolveScanPurchaseOrderTests(PostgresFixture fixture)
{
    private readonly Mock<IBarcodeService> _barcodes = new();

    private static int UniqueNumber() => Random.Shared.Next(1_000_000, 9_999_999);

    private async Task<ScanResolveResponseModel> ResolveAsync(string code)
    {
        await using var db = fixture.CreateContext();
        return await new ResolveScanHandler(db, _barcodes.Object)
            .Handle(new ResolveScanQuery(code), CancellationToken.None);
    }

    private async Task<List<int>> SeedPurchaseOrdersAsync(params string[] numbers)
    {
        await using var db = fixture.CreateContext();
        var vendor = new Vendor { CompanyName = "Steel Supply", IsActive = true };
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();
        var orders = numbers.Select(n => new PurchaseOrder { PONumber = n, VendorId = vendor.Id }).ToList();
        db.PurchaseOrders.AddRange(orders);
        await db.SaveChangesAsync();
        return orders.Select(o => o.Id).ToList();
    }

    [Fact]
    public async Task A_po_barcode_resolves_to_the_purchase_order_with_its_number_and_vendor()
    {
        var number = $"PO-{UniqueNumber()}";
        var ids = await SeedPurchaseOrdersAsync(number);
        _barcodes.Setup(b => b.FindByValueAsync(number, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Barcode { Value = number, EntityType = BarcodeEntityType.PurchaseOrder, PurchaseOrderId = ids[0] });

        var result = await ResolveAsync(number);

        result.Kind.Should().Be("purchaseOrder");
        result.Id.Should().Be(ids[0]);
        result.Label.Should().Be(number);
        result.Subtitle.Should().Be("Steel Supply");
    }

    [Fact]
    public async Task A_typed_po_number_resolves_case_insensitively()
    {
        var number = $"PO-{UniqueNumber()}";
        var ids = await SeedPurchaseOrdersAsync(number);

        var result = await ResolveAsync(number.ToLowerInvariant());

        result.Kind.Should().Be("purchaseOrder");
        result.Id.Should().Be(ids[0]);
        result.Label.Should().Be(number);
    }

    [Fact]
    public async Task A_po_number_without_the_prefix_resolves()
    {
        var number = $"V{UniqueNumber()}";
        var ids = await SeedPurchaseOrdersAsync(number);

        var result = await ResolveAsync(number);

        result.Kind.Should().Be("purchaseOrder");
        result.Id.Should().Be(ids[0]);
    }

    [Fact]
    public async Task A_po_prefixed_code_finds_a_number_stored_without_the_prefix()
    {
        var number = $"{UniqueNumber()}";
        var ids = await SeedPurchaseOrdersAsync(number);

        var result = await ResolveAsync($"PO-{number}");

        result.Kind.Should().Be("purchaseOrder");
        result.Id.Should().Be(ids[0]);
        result.Label.Should().Be(number);
    }

    [Fact]
    public async Task The_exact_number_wins_over_the_unprefixed_variant()
    {
        var n = UniqueNumber();
        var ids = await SeedPurchaseOrdersAsync($"{n}", $"PO-{n}");

        var result = await ResolveAsync($"po-{n}");

        result.Kind.Should().Be("purchaseOrder");
        result.Id.Should().Be(ids[1]);
    }

    [Fact]
    public async Task Like_wildcards_in_a_scanned_code_are_literal()
    {
        var n = UniqueNumber();
        await SeedPurchaseOrdersAsync($"PO-{n}");

        var result = await ResolveAsync($"PO-%{n}");

        result.Kind.Should().Be("unknown");
    }

    [Fact]
    public async Task A_po_whose_vendor_was_deleted_still_resolves_by_barcode_and_number()
    {
        var number = $"PO-{UniqueNumber()}";
        var ids = await SeedPurchaseOrdersAsync(number);
        await using (var db = fixture.CreateContext())
        {
            var vendorId = await db.PurchaseOrders.Where(p => p.Id == ids[0]).Select(p => p.VendorId).SingleAsync();
            var vendor = await db.Vendors.SingleAsync(v => v.Id == vendorId);
            vendor.DeletedAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
            await db.SaveChangesAsync();
        }

        var typed = await ResolveAsync(number);
        _barcodes.Setup(b => b.FindByValueAsync(number, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Barcode { Value = number, EntityType = BarcodeEntityType.PurchaseOrder, PurchaseOrderId = ids[0] });
        var scanned = await ResolveAsync(number);

        scanned.Kind.Should().Be("purchaseOrder");
        scanned.Id.Should().Be(ids[0]);
        scanned.Subtitle.Should().Be("Steel Supply");
        typed.Kind.Should().Be("purchaseOrder");
        typed.Id.Should().Be(ids[0]);
    }
}
