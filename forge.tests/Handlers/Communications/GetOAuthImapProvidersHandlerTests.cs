using FluentAssertions;
using Moq;

using Forge.Api.Features.Communications;

namespace Forge.Tests.Handlers.Communications;

public class GetOAuthImapProvidersHandlerTests
{
    [Fact]
    public async Task ReportsEachProvidersConfiguredState()
    {
        var oauth = new Mock<IImapOAuthService>();
        oauth.Setup(o => o.IsProviderConfiguredAsync("google", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        oauth.Setup(o => o.IsProviderConfiguredAsync("microsoft", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await new GetOAuthImapProvidersHandler(oauth.Object)
            .Handle(new GetOAuthImapProvidersQuery(), CancellationToken.None);

        result.Should().BeEquivalentTo([
            new OAuthImapProviderStatus("google", true),
            new OAuthImapProviderStatus("microsoft", false),
        ]);
    }
}
