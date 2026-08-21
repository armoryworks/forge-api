using Forge.Api.Bootstrap;

namespace Forge.Tests.Integrations;

/// <summary>
/// Resolution matrix for <see cref="IntegrationModeBootstrap.ResolveMode"/> — the pure
/// mock/real/auto/disabled decision that drives per-integration service registration.
/// </summary>
public class IntegrationModeBootstrapTests
{
    [Theory]
    // Explicit modes always win, regardless of configured-state or posture.
    [InlineData("Disabled", true, false, "Disabled")]
    [InlineData("Disabled", null, true, "Disabled")]
    [InlineData("Mock", true, false, "Mock")]
    [InlineData("Real", false, true, "Real")]
    // Auto resolves by configured-state, independent of posture.
    [InlineData("Auto", true, true, "Real")]     // configured → real even in a mock posture
    [InlineData("Auto", false, false, "Mock")]   // unconfigured → mock even in prod
    [InlineData("Auto", null, true, "Mock")]     // unknown config → follow posture (dev)
    [InlineData("Auto", null, false, "Real")]    // unknown config → follow posture (prod)
    // No mode row → the global flag is the posture.
    [InlineData(null, true, true, "Mock")]       // dev safety: mock even when configured
    [InlineData(null, false, true, "Mock")]
    [InlineData(null, true, false, "Real")]      // prod adopts real once configured
    [InlineData(null, false, false, "Mock")]     // prod, unconfigured → mock (readiness flags the gap)
    [InlineData(null, null, false, "Real")]      // prod, unknown provider → legacy real default
    public void ResolveMode_MatchesMatrix(string? explicitMode, bool? configured, bool globalMocks, string expected)
    {
        Assert.Equal(expected, IntegrationModeBootstrap.ResolveMode(explicitMode, configured, globalMocks));
    }

    [Fact]
    public void ResolveMode_IsCaseInsensitive()
    {
        Assert.Equal("Mock", IntegrationModeBootstrap.ResolveMode("mock", true, false));
        Assert.Equal("Real", IntegrationModeBootstrap.ResolveMode("REAL", false, true));
        Assert.Equal("Real", IntegrationModeBootstrap.ResolveMode("auto", true, true));
    }
}
