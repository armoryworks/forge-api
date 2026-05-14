using FluentAssertions;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

/// <summary>
/// Coverage for the IPartPricingResolver implementation. Walks each rung
/// (PriceListEntry → PartPrice → VendorPartPriceTier → Default) and
/// confirms it produces the expected price + provenance.
/// </summary>
public class PartPricingResolverTests
{
    private static Part NewPart(string partNumber) => new()
    {
        PartNumber = partNumber,
        Name = partNumber,
        ProcurementSource = ProcurementSource.Buy,
        InventoryClass = InventoryClass.Component,
        Status = PartStatus.Active,
    };

    [Fact]
    public async Task ResolveAsync_PriceListEntry_WinsWhenCustomerHasActiveListWithMatchingTier()
    {
        // Arrange — customer with active price list + entries at multiple
        // quantity tiers; resolver should pick the largest MinQuantity ≤ qty.
        using var db = TestDbContextFactory.Create();
        var part = NewPart("PRC-001");
        var customer = new Customer { Name = "Acme" };
        db.Parts.Add(part);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var priceList = new PriceList
        {
            Name = "Acme Wholesale",
            CustomerId = customer.Id,
            IsActive = true,
            EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-10),
            EffectiveTo = null,
        };
        db.PriceLists.Add(priceList);
        await db.SaveChangesAsync();

        db.PriceListEntries.AddRange(
            new PriceListEntry { PriceListId = priceList.Id, PartId = part.Id, MinQuantity = 1, UnitPrice = 12.50m },
            new PriceListEntry { PriceListId = priceList.Id, PartId = part.Id, MinQuantity = 100, UnitPrice = 9.99m });
        await db.SaveChangesAsync();

        var resolver = new PartPricingResolver(db);

        // Act — qty 150 should hit the MinQuantity=100 tier.
        var result = await resolver.ResolveAsync(part.Id, customer.Id, 150m, CancellationToken.None);

        // Assert
        result.Source.Should().Be(PartPriceSource.PriceListEntry);
        result.UnitPrice.Should().Be(9.99m);
        result.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task ResolveAsync_PartPrice_WinsWhenNoCustomerOrPriceListEntryHits()
    {
        // Arrange — only a PartPrice row (no customer / no PriceListEntry).
        using var db = TestDbContextFactory.Create();
        var part = NewPart("PRC-002");
        db.Parts.Add(part);
        await db.SaveChangesAsync();

        db.PartPrices.AddRange(
            new PartPrice
            {
                PartId = part.Id,
                UnitPrice = 22.00m,
                EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-30),
                Notes = "Initial",
            },
            new PartPrice
            {
                PartId = part.Id,
                UnitPrice = 24.50m,
                EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
                Notes = "Raw cost up",
            });
        await db.SaveChangesAsync();

        var resolver = new PartPricingResolver(db);

        // Act
        var result = await resolver.ResolveAsync(part.Id, customerId: null, quantity: null, CancellationToken.None);

        // Assert — latest EffectiveFrom wins.
        result.Source.Should().Be(PartPriceSource.PartPrice);
        result.UnitPrice.Should().Be(24.50m);
        result.Currency.Should().Be("USD");
        result.Notes.Should().Be("Raw cost up");
    }

    [Fact]
    public async Task ResolveAsync_VendorPartTier_WinsWhenNoPriceListEntryOrPartPrice()
    {
        // Arrange — preferred VendorPart with a couple of currently-effective tiers.
        using var db = TestDbContextFactory.Create();
        var part = NewPart("PRC-003");
        var vendor = new Vendor { CompanyName = "Supplier Inc" };
        db.Parts.Add(part);
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();

        var vp = new VendorPart
        {
            VendorId = vendor.Id,
            PartId = part.Id,
            IsPreferred = true,
        };
        db.VendorParts.Add(vp);
        await db.SaveChangesAsync();

        db.VendorPartPriceTiers.AddRange(
            new VendorPartPriceTier
            {
                VendorPartId = vp.Id,
                MinQuantity = 1m,
                UnitPrice = 5.00m,
                Currency = "USD",
                EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-10),
            },
            new VendorPartPriceTier
            {
                VendorPartId = vp.Id,
                MinQuantity = 100m,
                UnitPrice = 4.50m,
                Currency = "USD",
                EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-10),
            });
        await db.SaveChangesAsync();

        var resolver = new PartPricingResolver(db);

        // Act
        var result = await resolver.ResolveAsync(part.Id, customerId: null, quantity: null, CancellationToken.None);

        // Assert — lowest MinQuantity wins.
        result.Source.Should().Be(PartPriceSource.VendorPartTier);
        result.UnitPrice.Should().Be(5.00m);
        result.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task ResolveAsync_PartPrice_HonoursPartPriceCurrency()
    {
        // Arrange — Pillar 2 Dispatch B added Currency to PartPrice. The
        // resolver must echo it back rather than hardcoding USD.
        using var db = TestDbContextFactory.Create();
        var part = NewPart("PRC-EUR");
        db.Parts.Add(part);
        await db.SaveChangesAsync();

        db.PartPrices.Add(new PartPrice
        {
            PartId = part.Id,
            UnitPrice = 18.75m,
            Currency = "EUR",
            EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
            Notes = "Eurozone price",
        });
        await db.SaveChangesAsync();

        var resolver = new PartPricingResolver(db);

        // Act
        var result = await resolver.ResolveAsync(part.Id, customerId: null, quantity: null, CancellationToken.None);

        // Assert
        result.Source.Should().Be(PartPriceSource.PartPrice);
        result.UnitPrice.Should().Be(18.75m);
        result.Currency.Should().Be("EUR");
        result.Notes.Should().Be("Eurozone price");
    }

    [Fact]
    public async Task ResolveAsync_PriceListEntry_HonoursEntryCurrency()
    {
        // Arrange — Pillar 2 Dispatch B added Currency + Notes to
        // PriceListEntry. Resolver must echo them.
        using var db = TestDbContextFactory.Create();
        var part = NewPart("PRC-PLE-EUR");
        var customer = new Customer { Name = "Eurocorp" };
        db.Parts.Add(part);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var priceList = new PriceList
        {
            Name = "Eurocorp",
            CustomerId = customer.Id,
            IsActive = true,
            EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
        };
        db.PriceLists.Add(priceList);
        await db.SaveChangesAsync();

        db.PriceListEntries.Add(new PriceListEntry
        {
            PriceListId = priceList.Id,
            PartId = part.Id,
            MinQuantity = 1,
            UnitPrice = 21.50m,
            Currency = "EUR",
            Notes = "Customer-quoted EUR price",
        });
        await db.SaveChangesAsync();

        var resolver = new PartPricingResolver(db);

        // Act
        var result = await resolver.ResolveAsync(part.Id, customer.Id, quantity: 1m, CancellationToken.None);

        // Assert
        result.Source.Should().Be(PartPriceSource.PriceListEntry);
        result.UnitPrice.Should().Be(21.50m);
        result.Currency.Should().Be("EUR");
        result.Notes.Should().Be("Customer-quoted EUR price");
    }

    [Fact]
    public async Task ResolveAsync_VendorPartTier_HonoursTierCurrency()
    {
        // Arrange — preferred VendorPart whose tier carries non-USD currency.
        using var db = TestDbContextFactory.Create();
        var part = NewPart("PRC-CUR");
        var vendor = new Vendor { CompanyName = "Euro Supplier" };
        db.Parts.Add(part);
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();

        var vp = new VendorPart
        {
            VendorId = vendor.Id,
            PartId = part.Id,
            IsPreferred = true,
        };
        db.VendorParts.Add(vp);
        await db.SaveChangesAsync();

        db.VendorPartPriceTiers.Add(new VendorPartPriceTier
        {
            VendorPartId = vp.Id,
            MinQuantity = 1m,
            UnitPrice = 7.25m,
            Currency = "EUR",
            EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
        });
        await db.SaveChangesAsync();

        var resolver = new PartPricingResolver(db);

        var result = await resolver.ResolveAsync(part.Id, customerId: null, quantity: null, CancellationToken.None);

        result.Source.Should().Be(PartPriceSource.VendorPartTier);
        result.UnitPrice.Should().Be(7.25m);
        result.Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task ResolveAsync_Default_WhenNothingResolves()
    {
        // Arrange — naked Part, no pricing rows anywhere.
        using var db = TestDbContextFactory.Create();
        var part = NewPart("PRC-004");
        db.Parts.Add(part);
        await db.SaveChangesAsync();

        var resolver = new PartPricingResolver(db);

        // Act
        var result = await resolver.ResolveAsync(part.Id, customerId: null, quantity: null, CancellationToken.None);

        // Assert
        result.Source.Should().Be(PartPriceSource.Default);
        result.UnitPrice.Should().Be(0m);
        result.Currency.Should().Be("USD");
        result.SourceRowId.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_NonPreferredVendorPart_IsIgnored()
    {
        // Arrange — vendor part with tiers but NOT preferred. Resolver should
        // skip rung 3 and fall through to Default.
        using var db = TestDbContextFactory.Create();
        var part = NewPart("PRC-005");
        var vendor = new Vendor { CompanyName = "Backup" };
        db.Parts.Add(part);
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();

        var vp = new VendorPart
        {
            VendorId = vendor.Id,
            PartId = part.Id,
            IsPreferred = false,
        };
        db.VendorParts.Add(vp);
        await db.SaveChangesAsync();

        db.VendorPartPriceTiers.Add(new VendorPartPriceTier
        {
            VendorPartId = vp.Id,
            MinQuantity = 1m,
            UnitPrice = 99m,
            Currency = "USD",
            EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
        });
        await db.SaveChangesAsync();

        var resolver = new PartPricingResolver(db);

        var result = await resolver.ResolveAsync(part.Id, customerId: null, quantity: null, CancellationToken.None);

        result.Source.Should().Be(PartPriceSource.Default);
        result.UnitPrice.Should().Be(0m);
    }

    [Fact]
    public async Task ResolveManyAsync_SkipsRung1AndResolvesEachRungIndependently()
    {
        // Arrange — three parts, one for each non-rung1 outcome.
        using var db = TestDbContextFactory.Create();

        var partWithPartPrice = NewPart("BULK-PP");
        var partWithVendorTier = NewPart("BULK-VT");
        var partWithNothing = NewPart("BULK-NONE");
        db.Parts.AddRange(partWithPartPrice, partWithVendorTier, partWithNothing);
        await db.SaveChangesAsync();

        // PartPrice rung 2 row — latest of two effective rows.
        db.PartPrices.AddRange(
            new PartPrice
            {
                PartId = partWithPartPrice.Id,
                UnitPrice = 10m,
                EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-30),
            },
            new PartPrice
            {
                PartId = partWithPartPrice.Id,
                UnitPrice = 11m,
                EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
                Notes = "bump",
            });

        // Preferred VendorPart for the second part.
        var vendor = new Vendor { CompanyName = "Bulk Vendor" };
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();

        var vp = new VendorPart
        {
            VendorId = vendor.Id,
            PartId = partWithVendorTier.Id,
            IsPreferred = true,
        };
        db.VendorParts.Add(vp);
        await db.SaveChangesAsync();

        db.VendorPartPriceTiers.AddRange(
            new VendorPartPriceTier
            {
                VendorPartId = vp.Id,
                MinQuantity = 1m,
                UnitPrice = 4.25m,
                Currency = "CAD",
                EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
            },
            new VendorPartPriceTier
            {
                VendorPartId = vp.Id,
                MinQuantity = 50m,
                UnitPrice = 3.75m,
                Currency = "CAD",
                EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
            });
        await db.SaveChangesAsync();

        // Even if a customer/PriceList exists, the bulk variant should ignore it.
        var customer = new Customer { Name = "Skip-Me" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var priceList = new PriceList
        {
            Name = "Should be skipped",
            CustomerId = customer.Id,
            IsActive = true,
            EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
        };
        db.PriceLists.Add(priceList);
        await db.SaveChangesAsync();
        db.PriceListEntries.Add(new PriceListEntry
        {
            PriceListId = priceList.Id,
            PartId = partWithPartPrice.Id,
            MinQuantity = 1,
            UnitPrice = 1m, // Sentinel — should NOT win.
        });
        await db.SaveChangesAsync();

        var resolver = new PartPricingResolver(db);

        // Act
        var ids = new[] { partWithPartPrice.Id, partWithVendorTier.Id, partWithNothing.Id };
        var result = await resolver.ResolveManyAsync(ids, CancellationToken.None);

        // Assert
        result.Should().HaveCount(3);

        result[partWithPartPrice.Id].Source.Should().Be(PartPriceSource.PartPrice);
        result[partWithPartPrice.Id].UnitPrice.Should().Be(11m);
        result[partWithPartPrice.Id].Currency.Should().Be("USD");

        result[partWithVendorTier.Id].Source.Should().Be(PartPriceSource.VendorPartTier);
        result[partWithVendorTier.Id].UnitPrice.Should().Be(4.25m); // lowest MinQuantity
        result[partWithVendorTier.Id].Currency.Should().Be("CAD");

        result[partWithNothing.Id].Source.Should().Be(PartPriceSource.Default);
        result[partWithNothing.Id].UnitPrice.Should().Be(0m);
        result[partWithNothing.Id].Currency.Should().Be("USD");
    }

    [Fact]
    public async Task ResolveManyAsync_EmptyInput_ReturnsEmptyDictionary()
    {
        using var db = TestDbContextFactory.Create();
        var resolver = new PartPricingResolver(db);

        var result = await resolver.ResolveManyAsync(Array.Empty<int>(), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
