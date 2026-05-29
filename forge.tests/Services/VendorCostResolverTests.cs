using FluentAssertions;

using Forge.Api.Services;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

/// <summary>
/// UoM purchase-options effort — VendorCostResolver derives cost per base/stock unit from the
/// preferred vendor's price tier ÷ the purchase option's content quantity, picking the option
/// that's cheapest per base unit for the requested quantity. Pure LINQ over the context, so these
/// run on the InMemory provider.
/// </summary>
public class VendorCostResolverTests
{
    private static (AppDbContext db, int partId, int vendorPartId) SeedBase()
    {
        var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = $"P-{Guid.NewGuid():N}", Name = "Part" };
        var vendor = new Vendor { CompanyName = "Acme" };
        db.Parts.Add(part);
        db.Vendors.Add(vendor);
        db.SaveChanges();

        var vp = new VendorPart { PartId = part.Id, VendorId = vendor.Id, IsPreferred = true, Currency = "USD" };
        db.VendorParts.Add(vp);
        db.SaveChanges();

        return (db, part.Id, vp.Id);
    }

    private static VendorPartPriceTier Tier(int vendorPartId, decimal unitPrice, decimal minQty = 1, int? optionId = null)
        => new()
        {
            VendorPartId = vendorPartId,
            PurchaseOptionId = optionId,
            MinQuantity = minQty,
            UnitPrice = unitPrice,
            Currency = "USD",
            EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
        };

    [Fact]
    public async Task Resolves_per_base_unit_when_no_purchase_option()
    {
        var (db, partId, vpId) = SeedBase();
        db.VendorPartPriceTiers.Add(Tier(vpId, unitPrice: 5m));
        await db.SaveChangesAsync();

        var result = await new VendorCostResolver(db).ResolveAsync(partId, 10m, default);

        result.Resolved.Should().BeTrue();
        result.CostPerBaseUnit.Should().Be(5m);
        result.PurchaseOptionId.Should().BeNull();
        result.OptionContentQuantity.Should().Be(1m);
    }

    [Fact]
    public async Task Derives_per_each_cost_from_a_pack_option()
    {
        var (db, partId, vpId) = SeedBase();
        var bag = new PartPurchaseOption { PartId = partId, Label = "bag of 100", ContentQuantity = 100m };
        db.PartPurchaseOptions.Add(bag);
        await db.SaveChangesAsync();
        db.VendorPartPriceTiers.Add(Tier(vpId, unitPrice: 12m, optionId: bag.Id));
        await db.SaveChangesAsync();

        var result = await new VendorCostResolver(db).ResolveAsync(partId, 3m, default);

        result.Resolved.Should().BeTrue();
        result.CostPerBaseUnit.Should().Be(0.12m);   // $12 / 100 ea
        result.PurchaseOptionId.Should().Be(bag.Id);
        result.OptionContentQuantity.Should().Be(100m);
        result.TierUnitPrice.Should().Be(12m);
    }

    [Fact]
    public async Task Derives_per_sqft_cost_from_a_sheet_option()
    {
        var (db, partId, vpId) = SeedBase();
        var sheet = new PartPurchaseOption { PartId = partId, Label = "2x4 sheet", ContentQuantity = 8m };
        db.PartPurchaseOptions.Add(sheet);
        await db.SaveChangesAsync();
        db.VendorPartPriceTiers.Add(Tier(vpId, unitPrice: 50m, optionId: sheet.Id));
        await db.SaveChangesAsync();

        var result = await new VendorCostResolver(db).ResolveAsync(partId, 5m, default);

        result.CostPerBaseUnit.Should().Be(6.25m);   // $50 / 8 sqft
        result.PurchaseOptionId.Should().Be(sheet.Id);
    }

    [Fact]
    public async Task Picks_the_cheapest_option_per_base_unit()
    {
        var (db, partId, vpId) = SeedBase();
        var small = new PartPurchaseOption { PartId = partId, Label = "2x4 sheet", ContentQuantity = 8m };   // $14/8 = $1.75/sqft
        var big = new PartPurchaseOption { PartId = partId, Label = "4x8 sheet", ContentQuantity = 32m };    // $50/32 = $1.5625/sqft
        db.PartPurchaseOptions.AddRange(small, big);
        await db.SaveChangesAsync();
        db.VendorPartPriceTiers.AddRange(
            Tier(vpId, unitPrice: 14m, optionId: small.Id),
            Tier(vpId, unitPrice: 50m, optionId: big.Id));
        await db.SaveChangesAsync();

        var result = await new VendorCostResolver(db).ResolveAsync(partId, 100m, default);

        result.PurchaseOptionId.Should().Be(big.Id, "the 4×8 sheet is cheaper per sqft");
        result.CostPerBaseUnit.Should().Be(50m / 32m);
    }

    [Fact]
    public async Task Returns_unresolved_when_no_preferred_vendor_tier()
    {
        var (db, partId, _) = SeedBase();

        var result = await new VendorCostResolver(db).ResolveAsync(partId, 1m, default);

        result.Resolved.Should().BeFalse();
        result.CostPerBaseUnit.Should().Be(0m);
    }
}
