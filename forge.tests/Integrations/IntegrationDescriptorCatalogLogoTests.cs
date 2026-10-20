using FluentAssertions;

using Forge.Core.Settings;

namespace Forge.Tests.Integrations;

public class IntegrationDescriptorCatalogLogoTests
{
    [Fact]
    public void EveryLogoUrl_IsSameOriginOrNull()
    {
        foreach (var integration in IntegrationDescriptorCatalog.All)
        {
            if (integration.LogoUrl is null) continue;
            integration.LogoUrl.Should().StartWith("/", $"{integration.Provider} logo must load under img-src 'self'");
            integration.LogoUrl.Should().NotStartWith("//", $"{integration.Provider} logo must not be protocol-relative");
        }
    }
}
