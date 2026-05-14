using FluentAssertions;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

/// <summary>
/// Pillar 3 — coverage for the IPartSourcingResolver implementation.
/// Vendor-specific terms (lead time / MOQ / pack size) live exclusively
/// on the preferred VendorPart row; the per-column Part-snapshot fallback
/// was retired alongside the OEM-on-VendorPart move. When no preferred
/// VendorPart exists for a part the resolver returns null for every
/// vendor-specific value and consumers apply their own defaults.
/// </summary>
public class PartSourcingResolverTests
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
    public async Task ResolveAsync_NoVendorPart_ReturnsNullsAndUnresolvedFlag()
    {
        // Arrange — Part with no VendorPart rows at all.
        using var db = TestDbContextFactory.Create();
        var part = NewPart("NO-VP-001");
        db.Parts.Add(part);
        await db.SaveChangesAsync();

        var resolver = new PartSourcingResolver(db);

        // Act
        var result = await resolver.ResolveAsync(part.Id, CancellationToken.None);

        // Assert
        result.PartId.Should().Be(part.Id);
        result.PreferredVendorId.Should().BeNull();
        result.LeadTimeDays.Should().BeNull();
        result.MinOrderQty.Should().BeNull();
        result.PackSize.Should().BeNull();
        result.ResolvedFromVendorPart.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAsync_PreferredVendorPart_ReturnsItsValues()
    {
        // Arrange — preferred VendorPart with explicit values.
        using var db = TestDbContextFactory.Create();
        var vendor = new Vendor { CompanyName = "Acme Supply" };
        db.Vendors.Add(vendor);
        var part = NewPart("PREF-001");
        db.Parts.Add(part);
        await db.SaveChangesAsync();

        db.VendorParts.Add(new VendorPart
        {
            VendorId = vendor.Id,
            PartId = part.Id,
            IsPreferred = true,
            LeadTimeDays = 7,
            MinOrderQty = 100m,
            PackSize = 25m,
        });
        await db.SaveChangesAsync();

        var resolver = new PartSourcingResolver(db);

        // Act
        var result = await resolver.ResolveAsync(part.Id, CancellationToken.None);

        // Assert
        result.PreferredVendorId.Should().Be(vendor.Id);
        result.LeadTimeDays.Should().Be(7);
        result.MinOrderQty.Should().Be(100m);
        result.PackSize.Should().Be(25m);
        result.ResolvedFromVendorPart.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_PreferredVendorPartWithNulls_ReturnsNullsForUnsetColumns()
    {
        // Arrange — preferred VendorPart row exists but only sets one column.
        // The resolver reflects exactly what's on the row (no fallback path).
        using var db = TestDbContextFactory.Create();
        var vendor = new Vendor { CompanyName = "Partial Supply" };
        db.Vendors.Add(vendor);
        var part = NewPart("MIX-001");
        db.Parts.Add(part);
        await db.SaveChangesAsync();

        db.VendorParts.Add(new VendorPart
        {
            VendorId = vendor.Id,
            PartId = part.Id,
            IsPreferred = true,
            LeadTimeDays = 3,
            MinOrderQty = null,
            PackSize = null,
        });
        await db.SaveChangesAsync();

        var resolver = new PartSourcingResolver(db);

        // Act
        var result = await resolver.ResolveAsync(part.Id, CancellationToken.None);

        // Assert
        result.PreferredVendorId.Should().Be(vendor.Id);
        result.LeadTimeDays.Should().Be(3);
        result.MinOrderQty.Should().BeNull();
        result.PackSize.Should().BeNull();
        result.ResolvedFromVendorPart.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_NonPreferredVendorPart_IsIgnored()
    {
        // Arrange — VendorPart row exists but IsPreferred=false. The resolver
        // only looks at preferred rows, so this part has no resolved values.
        using var db = TestDbContextFactory.Create();
        var vendor = new Vendor { CompanyName = "Alt Vendor" };
        db.Vendors.Add(vendor);
        var part = NewPart("NONPREF-001");
        db.Parts.Add(part);
        await db.SaveChangesAsync();

        db.VendorParts.Add(new VendorPart
        {
            VendorId = vendor.Id,
            PartId = part.Id,
            IsPreferred = false,
            LeadTimeDays = 3,
            MinOrderQty = 999m,
            PackSize = 999m,
        });
        await db.SaveChangesAsync();

        var resolver = new PartSourcingResolver(db);

        // Act
        var result = await resolver.ResolveAsync(part.Id, CancellationToken.None);

        // Assert
        result.PreferredVendorId.Should().BeNull();
        result.LeadTimeDays.Should().BeNull();
        result.MinOrderQty.Should().BeNull();
        result.PackSize.Should().BeNull();
        result.ResolvedFromVendorPart.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveManyAsync_MixedParts_ResolvesEachIndependently()
    {
        // Arrange — three parts: one with no VendorPart, one with a preferred
        // VendorPart, one with only a non-preferred VendorPart.
        using var db = TestDbContextFactory.Create();
        var vendor = new Vendor { CompanyName = "Multi" };
        db.Vendors.Add(vendor);
        var noVp = NewPart("BULK-NONE");
        var pref = NewPart("BULK-PREF");
        var nonPref = NewPart("BULK-NONPREF");
        db.Parts.AddRange(noVp, pref, nonPref);
        await db.SaveChangesAsync();

        db.VendorParts.AddRange(
            new VendorPart
            {
                VendorId = vendor.Id, PartId = pref.Id, IsPreferred = true,
                LeadTimeDays = 4, MinOrderQty = 99m, PackSize = 11m,
            },
            new VendorPart
            {
                VendorId = vendor.Id, PartId = nonPref.Id, IsPreferred = false,
                LeadTimeDays = 99, MinOrderQty = 999m, PackSize = 999m,
            });
        await db.SaveChangesAsync();

        var resolver = new PartSourcingResolver(db);

        // Act
        var result = await resolver.ResolveManyAsync(
            new[] { noVp.Id, pref.Id, nonPref.Id },
            CancellationToken.None);

        // Assert
        result.Should().ContainKey(noVp.Id);
        result[noVp.Id].LeadTimeDays.Should().BeNull();
        result[noVp.Id].PackSize.Should().BeNull();
        result[noVp.Id].ResolvedFromVendorPart.Should().BeFalse();

        result[pref.Id].LeadTimeDays.Should().Be(4);
        result[pref.Id].MinOrderQty.Should().Be(99m);
        result[pref.Id].PackSize.Should().Be(11m);
        result[pref.Id].ResolvedFromVendorPart.Should().BeTrue();

        result[nonPref.Id].LeadTimeDays.Should().BeNull();
        result[nonPref.Id].MinOrderQty.Should().BeNull();
        result[nonPref.Id].PackSize.Should().BeNull();
        result[nonPref.Id].ResolvedFromVendorPart.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveManyAsync_EmptyInput_ReturnsEmptyDictionary()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        var resolver = new PartSourcingResolver(db);

        // Act
        var result = await resolver.ResolveManyAsync(Array.Empty<int>(), CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}
