using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Forge.Api.Capabilities;
using Forge.Api.Services.Cpq;
using Forge.Api.Services.Fulfilment;
using Forge.Api.Services.Unavailable;
using Forge.Core.Interfaces;
using Forge.Tests.Helpers;

namespace Forge.Tests.Capabilities;

/// <summary>
/// The CPQ, drop-ship and back-to-back engines the app resolves: the built-in engine while its capability is on,
/// the 501 "not available" stand-in while it is off.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public sealed class SalesOrderEngineRegistrationTests(CapabilityTestWebApplicationFactory factory)
{
    [Fact]
    public void With_the_capabilities_off_each_engine_answers_not_available()
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var capabilities = services.GetRequiredService<ICapabilitySnapshotProvider>();
        capabilities.IsEnabled("CAP-O2C-CPQ").Should().BeFalse("CPQ is off by default");

        services.GetRequiredService<ICpqService>().Should().BeOfType<UnavailableCpqService>();
        services.GetRequiredService<IDropShipService>().Should().BeOfType<UnavailableDropShipService>();
        services.GetRequiredService<IBackToBackService>().Should().BeOfType<UnavailableBackToBackService>();
    }

    [Fact]
    public void With_the_capabilities_on_each_engine_is_the_built_in_one()
    {
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<ICapabilitySnapshotProvider>(
                new StubCapabilitySnapshotProvider("CAP-O2C-CPQ", "CAP-P2P-DROPSHIP", "CAP-P2P-BACKTOBACK"))));
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;

        services.GetRequiredService<ICpqService>().Should().BeOfType<BuiltInCpqService>();
        services.GetRequiredService<IDropShipService>().Should().BeOfType<BuiltInDropShipService>();
        services.GetRequiredService<IBackToBackService>().Should().BeOfType<BuiltInBackToBackService>();
    }
}
