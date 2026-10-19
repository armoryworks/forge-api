using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;

namespace Forge.Tests.Services;

public class UserIntegrationConnectionTesterTests
{
    private const int UserId = 7;
    private const int IntegrationId = 42;

    private readonly Mock<IUserIntegrationService> _store = new();

    private void StoreHas(string providerId) =>
        _store.Setup(s => s.GetByIdAsync(UserId, IntegrationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserIntegration { Id = IntegrationId, UserId = UserId, ProviderId = providerId });

    private void WebhookIs(string url) =>
        _store.Setup(s => s.GetDecryptedCredentialsAsync(UserId, IntegrationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync($"{{\"webhook_url\":\"{url}\"}}");

    private static Mock<IMessagingIntegrationService> Messaging(string providerId, bool result)
    {
        var m = new Mock<IMessagingIntegrationService>();
        m.SetupGet(s => s.ProviderId).Returns(providerId);
        m.Setup(s => s.TestConnectionAsync(UserId, IntegrationId, It.IsAny<CancellationToken>())).ReturnsAsync(result);
        return m;
    }

    [Fact]
    public async Task Test_MessagingProvider_CallsTheProviderAndRecordsFailure()
    {
        StoreHas("slack");
        WebhookIs("https://hooks.slack.com/services/T0/B0/x");
        var slack = Messaging("slack", result: false);
        var tester = new UserIntegrationConnectionTester(_store.Object, [Messaging("teams", true).Object, slack.Object], []);

        var result = await tester.TestAsync(UserId, IntegrationId);

        result.Should().Be(new UserIntegrationTestResult(Success: false, Contacted: true));
        slack.Verify(s => s.TestConnectionAsync(UserId, IntegrationId, It.IsAny<CancellationToken>()), Times.Once);
        _store.Verify(s => s.TestConnectionAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _store.Verify(s => s.UpdateSyncStatusAsync(IntegrationId, null, It.Is<string?>(e => e != null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("slack", "http://hooks.slack.com/services/T0/B0/x")]
    [InlineData("slack", "https://forge-db:5432/")]
    [InlineData("slack", "https://hooks.slack.com.attacker.example/x")]
    [InlineData("discord", "https://169.254.169.254/latest/meta-data")]
    [InlineData("teams", "https://localhost/hook")]
    [InlineData("google_chat", "not a url")]
    public async Task Test_WebhookOutsideTheProvidersHosts_IsNotContacted(string providerId, string url)
    {
        StoreHas(providerId);
        WebhookIs(url);
        var messaging = Messaging(providerId, result: true);
        var tester = new UserIntegrationConnectionTester(_store.Object, [messaging.Object], []);

        var result = await tester.TestAsync(UserId, IntegrationId);

        result.Should().Be(new UserIntegrationTestResult(Success: false, Contacted: false));
        messaging.Verify(s => s.TestConnectionAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _store.Verify(s => s.UpdateSyncStatusAsync(IntegrationId, null, It.Is<string?>(e => e != null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("discord", "https://discord.com/api/webhooks/1/abc")]
    [InlineData("google_chat", "https://chat.googleapis.com/v1/spaces/AAA/messages?key=k")]
    [InlineData("teams", "https://contoso.webhook.office.com/webhookb2/abc")]
    [InlineData("teams", "https://prod-01.westus.logic.azure.com/workflows/abc")]
    public async Task Test_WebhookOnTheProvidersHost_IsContacted(string providerId, string url)
    {
        StoreHas(providerId);
        WebhookIs(url);
        var messaging = Messaging(providerId, result: true);
        var tester = new UserIntegrationConnectionTester(_store.Object, [messaging.Object], []);

        var result = await tester.TestAsync(UserId, IntegrationId);

        result.Should().Be(new UserIntegrationTestResult(Success: true, Contacted: true));
    }

    [Fact]
    public async Task Test_CalendarProvider_CallsTheProvider()
    {
        StoreHas("google_calendar");
        var calendar = new Mock<ICalendarIntegrationService>();
        calendar.SetupGet(s => s.ProviderId).Returns("google_calendar");
        calendar.Setup(s => s.TestConnectionAsync(UserId, IntegrationId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var tester = new UserIntegrationConnectionTester(_store.Object, [], [calendar.Object]);

        var result = await tester.TestAsync(UserId, IntegrationId);

        result.Should().Be(new UserIntegrationTestResult(Success: true, Contacted: true));
        _store.Verify(s => s.UpdateSyncStatusAsync(IntegrationId, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Test_ProviderWithoutALiveTest_ReportsThatNothingWasContacted()
    {
        StoreHas("s3");
        _store.Setup(s => s.TestConnectionAsync(UserId, IntegrationId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var tester = new UserIntegrationConnectionTester(_store.Object, [Messaging("mock_messaging", true).Object], []);

        var result = await tester.TestAsync(UserId, IntegrationId);

        result.Should().Be(new UserIntegrationTestResult(Success: true, Contacted: false));
    }

    [Fact]
    public async Task Test_UnknownIntegration_Throws()
    {
        var tester = new UserIntegrationConnectionTester(_store.Object, [], []);

        var act = () => tester.TestAsync(UserId, IntegrationId);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData("google_drive")]
    [InlineData("onedrive")]
    [InlineData("dropbox")]
    [InlineData("github")]
    public void Catalog_DoesNotOfferProvidersNothingConsumes(string providerId)
    {
        var service = new UserIntegrationService(
            null!, Mock.Of<ITokenEncryptionService>(), Mock.Of<IActivityLogRepository>(),
            NullLogger<UserIntegrationService>.Instance);

        service.GetAvailableProviders().Select(p => p.ProviderId).Should().NotContain(providerId);
    }
}
