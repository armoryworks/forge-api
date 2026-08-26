using FluentAssertions;

using Forge.Api.Capabilities;

namespace Forge.Tests.Capabilities;

/// <summary>
/// The per-screen mobile flags must hang off CAP-MOBILE-CORE: each
/// MobileController action is gated on its own screen flag (the closest
/// attribute wins), so the dependency edge is what keeps the core from
/// being switched off underneath an enabled screen.
/// </summary>
public class MobileCapabilityRelationsTests
{
    private static readonly string[] Screens =
        ["CAP-MOBILE-SCAN", "CAP-MOBILE-CLOCK", "CAP-MOBILE-JOBS", "CAP-MOBILE-STOCK", "CAP-MOBILE-LOOKUP"];

    [Fact]
    public void Every_screen_flag_is_in_the_catalog_and_off_by_default()
    {
        foreach (var code in Screens)
        {
            var definition = CapabilityCatalog.All.SingleOrDefault(c => c.Code == code);
            definition.Should().NotBeNull(code);
            definition!.IsDefaultOn.Should().BeFalse(code);
            definition.Area.Should().Be("MOBILE");
        }
    }

    [Fact]
    public void Every_screen_flag_depends_on_the_core()
    {
        foreach (var code in Screens)
        {
            CapabilityCatalogRelations.Dependencies
                .Should().Contain(e => e.From == code && e.To == "CAP-MOBILE-CORE", code);
        }
    }

    [Fact]
    public void Disabling_the_core_is_blocked_while_any_screen_is_on()
    {
        var enabled = Screens.Append("CAP-MOBILE-CORE").ToDictionary(c => c, _ => true);

        var dependents = CapabilityDependencyResolver.FindEnabledDependents("CAP-MOBILE-CORE", enabled);

        dependents.Should().BeEquivalentTo(Screens);
    }
}
