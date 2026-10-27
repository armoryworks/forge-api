using System.Security.Cryptography;
using System.Text;

using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

using Forge.Api.Features.ComplianceForms;
using Forge.Core.Models;
using Forge.Core.Settings;

namespace Forge.Tests.Handlers.ComplianceForms;

public class DocuSealWebhookSecretSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private const string Body = """{"event_type":"form.completed","data":{"id":9001}}""";

    private static DocuSealWebhookSecretSource Source(string? saved, string configured)
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetStringAsync(DocuSealSettings.KeyWebhookSecret, It.IsAny<CancellationToken>())).ReturnsAsync(saved);
        return new DocuSealWebhookSecretSource(settings.Object, Options.Create(new DocuSealOptions { WebhookSecret = configured }));
    }

    private static string Sign(string secret)
    {
        var ts = Now.ToUnixTimeSeconds();
        var hex = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{ts}.{Body}")));
        return $"{ts}.{hex.ToLowerInvariant()}";
    }

    [Fact]
    public async Task The_secret_saved_in_admin_integrations_verifies_a_signed_webhook_over_the_configured_one()
    {
        var secret = await Source("saved-in-admin", "from-env").GetAsync(CancellationToken.None);

        secret.Should().Be("saved-in-admin");
        DocuSealWebhookSignature.IsValid(secret!, Body, Sign("saved-in-admin"), Now).Should().BeTrue();
        DocuSealWebhookSignature.IsValid(secret!, Body, Sign("from-env"), Now).Should().BeFalse();
    }

    [Fact]
    public async Task Without_a_saved_secret_the_configuration_value_is_used()
    {
        (await Source(null, "from-env").GetAsync(CancellationToken.None)).Should().Be("from-env");
    }

    [Fact]
    public async Task With_neither_the_webhook_has_no_secret()
    {
        (await Source("", "").GetAsync(CancellationToken.None)).Should().BeNull();
    }
}
