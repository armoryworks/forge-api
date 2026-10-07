using System.Reflection;

using FluentAssertions;

using Forge.Api.Capabilities;
using Forge.Api.Controllers;

namespace Forge.Tests.Capabilities;

/// <summary>
/// The browser phone view (/m) resolves scans through the scanner route, so
/// that route must stay on a capability a default install has switched on,
/// unlike the native app's CAP-MOBILE-SCAN.
/// </summary>
public class ScannerResolveCapabilityTests
{
    [Fact]
    public void Scanner_resolve_is_gated_by_a_default_on_capability()
    {
        var action = typeof(ScannerController).GetMethod(nameof(ScannerController.Resolve))!;
        var gate = action.GetCustomAttribute<RequiresCapabilityAttribute>()
            ?? typeof(ScannerController).GetCustomAttribute<RequiresCapabilityAttribute>();

        gate.Should().NotBeNull();
        var definition = CapabilityCatalog.All.Single(c => c.Code == gate!.Capability);
        definition.IsDefaultOn.Should().BeTrue(definition.Code);
    }
}
