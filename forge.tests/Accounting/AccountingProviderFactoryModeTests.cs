using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Integrations;

namespace Forge.Tests.Accounting;

/// <summary>
/// The accounting-boundary decision reads the ACTIVE provider from the
/// <c>accounting_provider</c> setting, never whichever <see cref="IAccountingService"/>
/// DI happens to inject (the mock answers its probe with true, the last real
/// registration is Zoho). Standalone installs must stay standalone even when the
/// registered service would answer its probe.
/// </summary>
public class AccountingProviderFactoryModeTests
{
    private static Mock<IAccountingService> Provider(string id, bool connected)
    {
        var provider = new Mock<IAccountingService>();
        provider.Setup(p => p.ProviderId).Returns(id);
        provider.Setup(p => p.TestConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(connected);
        return provider;
    }

    private static AccountingProviderFactory Factory(string? activeProviderId, params IAccountingService[] providers)
    {
        var settings = new Mock<ISystemSettingRepository>();
        settings.Setup(s => s.FindByKeyAsync("accounting_provider", It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeProviderId is null
                ? null
                : new SystemSetting { Key = "accounting_provider", Value = activeProviderId });
        return new AccountingProviderFactory(providers, settings.Object, NullLogger<AccountingProviderFactory>.Instance);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("local")]
    [InlineData("forge-native")]
    public async Task Standalone_settings_have_no_connected_external_provider(string? activeProviderId)
    {
        var mock = Provider("mock", connected: true);
        var local = Provider("local", connected: true);
        var native = Provider("forge-native", connected: true);

        var factory = Factory(activeProviderId, mock.Object, local.Object, native.Object);

        (await factory.GetConnectedExternalProviderAsync(CancellationToken.None)).Should().BeNull();
        mock.Verify(p => p.TestConnectionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Selected_external_provider_that_answers_its_probe_is_returned()
    {
        var quickBooks = Provider("quickbooks", connected: true);
        var zoho = Provider("zoho", connected: false);

        var factory = Factory("quickbooks", quickBooks.Object, zoho.Object);

        (await factory.GetConnectedExternalProviderAsync(CancellationToken.None)).Should().BeSameAs(quickBooks.Object);
    }

    [Fact]
    public async Task Selected_external_provider_that_fails_its_probe_is_not_connected()
    {
        var quickBooks = Provider("quickbooks", connected: false);

        var factory = Factory("quickbooks", quickBooks.Object);

        (await factory.GetConnectedExternalProviderAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Under_mock_integrations_a_selected_provider_resolves_to_the_mock()
    {
        var mock = Provider("mock", connected: true);

        var factory = Factory("quickbooks", mock.Object);

        (await factory.GetConnectedExternalProviderAsync(CancellationToken.None)).Should().BeSameAs(mock.Object);
    }

    [Fact]
    public async Task Unregistered_provider_without_a_mock_is_not_connected()
    {
        var factory = Factory("quickbooks", Provider("zoho", connected: true).Object);

        (await factory.GetConnectedExternalProviderAsync(CancellationToken.None)).Should().BeNull();
    }
}
