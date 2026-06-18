using FluentAssertions;

using Forge.Api.Capabilities;

namespace Forge.Tests.Capabilities;

public class ModuleCatalogTests
{
    private static IReadOnlyDictionary<string, bool> EnabledMap(IReadOnlySet<string> enabled) =>
        CapabilityCatalog.All.ToDictionary(c => c.Code, c => enabled.Contains(c.Code));

    [Fact]
    public void InventoryOnly_enablesInventoryAndFoundations_butNotOtherModules()
    {
        var set = ModuleCatalog.EnabledCapabilitiesFor(new[] { "inventory" });

        set.Should().Contain("CAP-INV-CORE");
        set.Should().Contain("CAP-INV-ADJUST");
        set.Should().Contain("CAP-MD-PARTS");      // foundation
        set.Should().Contain("CAP-IDEN-USERS");    // foundation

        set.Should().NotContain("CAP-O2C-SO");          // sales not selected
        set.Should().NotContain("CAP-P2P-PO");          // purchasing not selected
        set.Should().NotContain("CAP-MFG-WO-RELEASE");  // production not selected
        set.Should().NotContain("CAP-EXT-KANBAN");      // job board belongs to production
        // Customers/Vendors are owned by Sales/Purchasing, not Foundations — an
        // inventory-only install must not surface them (they were previously pulled
        // in by CAP-RPT-OPERATIONAL, now removed from Foundations).
        set.Should().NotContain("CAP-MD-CUSTOMERS");
        set.Should().NotContain("CAP-MD-VENDORS");
    }

    [Fact]
    public void Foundations_areAlwaysIncluded_evenWithNoModules()
    {
        var set = ModuleCatalog.EnabledCapabilitiesFor(System.Array.Empty<string>());
        set.Should().Contain(ModuleCatalog.Foundations);
    }

    [Fact]
    public void UnknownModuleIds_areIgnored()
    {
        var set = ModuleCatalog.EnabledCapabilitiesFor(new[] { "does-not-exist" });
        set.Should().Contain(ModuleCatalog.Foundations); // still just foundations
        set.Should().NotContain("CAP-INV-MULTILOC");
    }

    [Theory]
    [InlineData("inventory")]
    [InlineData("purchasing")]
    [InlineData("sales")]
    [InlineData("production")]
    [InlineData("shipping")]
    [InlineData("invoicing")]
    [InlineData("quality")]
    [InlineData("planning")]
    [InlineData("people")]
    public void EachModule_resolvesToADependencyCompleteSet(string moduleId)
    {
        // The resolved set must have every prerequisite present, or applying it
        // would be rejected by the capability gate at runtime.
        var set = ModuleCatalog.EnabledCapabilitiesFor(new[] { moduleId });
        var enabled = EnabledMap(set);

        foreach (var code in set)
        {
            CapabilityDependencyResolver.FindMissingDependencies(code, enabled)
                .Should().BeEmpty($"{code} (pulled in by module '{moduleId}') must have its prerequisites enabled");
        }
    }

    [Fact]
    public void AllModulesTogether_haveNoMissingDepsAndNoMutexConflicts()
    {
        var ids = ModuleCatalog.All.Select(m => m.Id).ToList();
        var set = ModuleCatalog.EnabledCapabilitiesFor(ids);
        var enabled = EnabledMap(set);

        foreach (var code in set)
        {
            CapabilityDependencyResolver.FindMissingDependencies(code, enabled).Should().BeEmpty();
            CapabilityDependencyResolver.FindEnabledMutexConflicts(code, enabled).Should().BeEmpty();
        }
    }

    [Fact]
    public void EveryModuleCapabilityCode_existsInTheCatalog()
    {
        var known = CapabilityCatalog.All.Select(c => c.Code).ToHashSet();
        var referenced = ModuleCatalog.Foundations
            .Concat(ModuleCatalog.All.SelectMany(m => m.Capabilities))
            .Distinct();

        referenced.Should().OnlyContain(code => known.Contains(code));
    }
}
