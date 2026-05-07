using FluentAssertions;
using Moq;

using QBEngineer.Api.Features.VendorParts;
using QBEngineer.Core.Entities;
using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models;
using QBEngineer.Integrations;
using QBEngineer.Tests.Helpers;

namespace QBEngineer.Tests.Handlers.VendorParts;

/// <summary>
/// Bought-parts effort PR4 — variance check tests cover threshold
/// resolution (vendor override → setting → 5% default), tier-pick at qty,
/// the no-VendorPart and no-tier paths, and zero-tier handling.
/// </summary>
public class CheckTierVarianceHandlerTests
{
    private readonly Mock<ISystemSettingRepository> _settingRepo = new();
    private readonly IClock _clock = new SystemClock();

    public CheckTierVarianceHandlerTests()
    {
        // Default fallback: setting absent → handler uses 5%.
        _settingRepo.Setup(r => r.FindByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SystemSetting?)null);
    }

    [Fact]
    public async Task Handle_NoVendorPartForLine_FlagsOffTier()
    {
        var db = TestDbContextFactory.Create();
        db.Vendors.Add(new Vendor { Id = 1, CompanyName = "Acme", OffTierVariancePct = null });
        await db.SaveChangesAsync();

        var handler = new CheckTierVarianceHandler(db, _settingRepo.Object, _clock);
        var result = await handler.Handle(
            new CheckTierVarianceQuery(1, [new(PartId: 99, Quantity: 1m, UnitPrice: 10m)]),
            CancellationToken.None);

        result.ThresholdPct.Should().Be(5m);
        result.Lines.Should().HaveCount(1);
        result.Lines[0].IsOffTier.Should().BeTrue();
        result.Lines[0].TierPrice.Should().BeNull();
        result.Lines[0].VendorPartId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_PriceWithinThreshold_NotFlagged()
    {
        var db = TestDbContextFactory.Create();
        db.Vendors.Add(new Vendor { Id = 1, CompanyName = "Acme" });
        var vp = new VendorPart { Id = 100, VendorId = 1, PartId = 99, Currency = "USD" };
        vp.PriceTiers.Add(new VendorPartPriceTier
        {
            VendorPartId = 100, MinQuantity = 1m, UnitPrice = 10m, Currency = "USD",
            EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
        });
        db.VendorParts.Add(vp);
        await db.SaveChangesAsync();

        var handler = new CheckTierVarianceHandler(db, _settingRepo.Object, _clock);
        // Entered $10.30 vs tier $10 = 3% variance (under 5%).
        var result = await handler.Handle(
            new CheckTierVarianceQuery(1, [new(PartId: 99, Quantity: 5m, UnitPrice: 10.30m)]),
            CancellationToken.None);

        result.Lines[0].IsOffTier.Should().BeFalse();
        result.Lines[0].TierPrice.Should().Be(10m);
        result.Lines[0].VariancePct.Should().BeApproximately(3m, 0.01m);
    }

    [Fact]
    public async Task Handle_PriceBeyondThreshold_Flagged()
    {
        var db = TestDbContextFactory.Create();
        db.Vendors.Add(new Vendor { Id = 1, CompanyName = "Acme" });
        var vp = new VendorPart { Id = 100, VendorId = 1, PartId = 99, Currency = "USD" };
        vp.PriceTiers.Add(new VendorPartPriceTier
        {
            VendorPartId = 100, MinQuantity = 1m, UnitPrice = 10m, Currency = "USD",
            EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
        });
        db.VendorParts.Add(vp);
        await db.SaveChangesAsync();

        var handler = new CheckTierVarianceHandler(db, _settingRepo.Object, _clock);
        // Entered $11.50 vs tier $10 = 15% variance (over 5%).
        var result = await handler.Handle(
            new CheckTierVarianceQuery(1, [new(PartId: 99, Quantity: 5m, UnitPrice: 11.50m)]),
            CancellationToken.None);

        result.Lines[0].IsOffTier.Should().BeTrue();
        result.Lines[0].VariancePct.Should().BeApproximately(15m, 0.01m);
    }

    [Fact]
    public async Task Handle_VendorOverrideUsesWiderThreshold()
    {
        var db = TestDbContextFactory.Create();
        // Vendor allows up to 25% variance — quirky pricing shouldn't trigger prompts.
        db.Vendors.Add(new Vendor { Id = 1, CompanyName = "Quirky", OffTierVariancePct = 25m });
        var vp = new VendorPart { Id = 100, VendorId = 1, PartId = 99, Currency = "USD" };
        vp.PriceTiers.Add(new VendorPartPriceTier
        {
            VendorPartId = 100, MinQuantity = 1m, UnitPrice = 10m, Currency = "USD",
            EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
        });
        db.VendorParts.Add(vp);
        await db.SaveChangesAsync();

        var handler = new CheckTierVarianceHandler(db, _settingRepo.Object, _clock);
        // 15% variance — above the 5% default, but under the 25% vendor override.
        var result = await handler.Handle(
            new CheckTierVarianceQuery(1, [new(PartId: 99, Quantity: 5m, UnitPrice: 11.50m)]),
            CancellationToken.None);

        result.ThresholdPct.Should().Be(25m);
        result.Lines[0].IsOffTier.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_PicksLargestMinQtyTierUnderRequest()
    {
        var db = TestDbContextFactory.Create();
        db.Vendors.Add(new Vendor { Id = 1, CompanyName = "Acme" });
        var vp = new VendorPart { Id = 100, VendorId = 1, PartId = 99, Currency = "USD" };
        var now = DateTimeOffset.UtcNow.AddDays(-1);
        // Three tiers: 1+ @ $10, 100+ @ $9, 500+ @ $8. Request qty 250 → picks 100+ tier.
        vp.PriceTiers.Add(new VendorPartPriceTier { VendorPartId = 100, MinQuantity = 1m, UnitPrice = 10m, Currency = "USD", EffectiveFrom = now });
        vp.PriceTiers.Add(new VendorPartPriceTier { VendorPartId = 100, MinQuantity = 100m, UnitPrice = 9m, Currency = "USD", EffectiveFrom = now });
        vp.PriceTiers.Add(new VendorPartPriceTier { VendorPartId = 100, MinQuantity = 500m, UnitPrice = 8m, Currency = "USD", EffectiveFrom = now });
        db.VendorParts.Add(vp);
        await db.SaveChangesAsync();

        var handler = new CheckTierVarianceHandler(db, _settingRepo.Object, _clock);
        var result = await handler.Handle(
            new CheckTierVarianceQuery(1, [new(PartId: 99, Quantity: 250m, UnitPrice: 9m)]),
            CancellationToken.None);

        result.Lines[0].TierPrice.Should().Be(9m);
        result.Lines[0].IsOffTier.Should().BeFalse();
        result.Lines[0].VariancePct.Should().Be(0m);
    }
}
