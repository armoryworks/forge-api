using System.Net;
using System.Text.Json;

using FluentAssertions;
using FluentValidation.TestHelper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Webhooks;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Webhooks;

public class WebhookAdminTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IClock> _clock = new();
    private readonly IDataProtectionProvider _dataProtection = new EphemeralDataProtectionProvider();

    public WebhookAdminTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
    }

    private WebhookSubscription AddSubscription(bool active = true, int failures = 0)
    {
        var subscription = new WebhookSubscription
        {
            Url = "http://hooks.example.test/in",
            EventTypesJson = """["invoice.created"]""",
            EncryptedSecret = _dataProtection.CreateProtector(WebhookDeliveryService.SecretPurpose).Protect("shared-secret"),
            MaxRetries = 3,
            IsActive = active,
            FailureCount = failures,
        };
        _db.WebhookSubscriptions.Add(subscription);
        _db.SaveChanges();
        return subscription;
    }

    private WebhookManualDelivery Manual(HttpStatusCode status, List<string>? bodies = null) =>
        new(_db, new WebhookDeliveryService(
            _db, new StubHttpClientFactory(new RecordingHandler(status, bodies ?? [])), _dataProtection, _clock.Object,
            NullLogger<WebhookDeliveryService>.Instance));

    [Fact]
    public async Task Creating_without_a_secret_generates_one_and_shows_it_once()
    {
        var handler = new CreateWebhookSubscriptionHandler(_db, _dataProtection);

        var created = await handler.Handle(new CreateWebhookSubscriptionCommand(
            "https://hooks.example.test/in", """["job.created"]""", null, null, null, 3, true), CancellationToken.None);

        created.Secret.Should().StartWith("whsec_");
        var stored = _db.WebhookSubscriptions.Single();
        _dataProtection.CreateProtector(WebhookDeliveryService.SecretPurpose).Unprotect(stored.EncryptedSecret).Should().Be(created.Secret);
        (await new GetWebhookSubscriptionsHandler(_db).Handle(new GetWebhookSubscriptionsQuery(), CancellationToken.None))
            .Single().Secret.Should().BeNull();
    }

    [Fact]
    public async Task Editing_changes_url_events_and_reactivating_clears_the_failure_count()
    {
        var subscription = AddSubscription(active: false, failures: 6);
        var handler = new UpdateWebhookSubscriptionHandler(_db, _dataProtection);

        var result = await handler.Handle(new UpdateWebhookSubscriptionCommand(
            subscription.Id, "https://hooks.example.test/v2", """["job.created","invoice.created"]""",
            "ERP bridge", """{"X-Tenant":"plant-1"}""", 5, false, true, false), CancellationToken.None);

        result.Url.Should().Be("https://hooks.example.test/v2");
        result.IsActive.Should().BeTrue();
        result.FailureCount.Should().Be(0);
        result.HeadersJson.Should().Be("""{"X-Tenant":"plant-1"}""");
        result.Secret.Should().BeNull();
    }

    [Fact]
    public async Task Rotating_the_secret_returns_the_new_one_and_seals_it()
    {
        var subscription = AddSubscription();
        var handler = new UpdateWebhookSubscriptionHandler(_db, _dataProtection);

        var result = await handler.Handle(new UpdateWebhookSubscriptionCommand(
            subscription.Id, subscription.Url, subscription.EventTypesJson, null, null, 3, true, true, true), CancellationToken.None);

        result.Secret.Should().NotBeNullOrEmpty().And.NotBe("shared-secret");
        _dataProtection.CreateProtector(WebhookDeliveryService.SecretPurpose)
            .Unprotect(_db.WebhookSubscriptions.Single().EncryptedSecret).Should().Be(result.Secret);
    }

    [Theory]
    [InlineData("ftp://hooks.example.test", """["job.created"]""", nameof(UpdateWebhookSubscriptionCommand.Url))]
    [InlineData("https://hooks.example.test", """["job.made"]""", nameof(UpdateWebhookSubscriptionCommand.EventTypesJson))]
    public void Edits_are_validated_like_creates(string url, string events, string property)
    {
        var result = new UpdateWebhookSubscriptionCommandValidator().TestValidate(
            new UpdateWebhookSubscriptionCommand(1, url, events, null, null, 3, true, true, false));

        result.ShouldHaveValidationErrorFor(property);
    }

    [Fact]
    public async Task Deleting_soft_deletes_and_stops_deliveries()
    {
        var subscription = AddSubscription();

        await new DeleteWebhookSubscriptionHandler(_db, _clock.Object)
            .Handle(new DeleteWebhookSubscriptionCommand(subscription.Id), CancellationToken.None);

        _db.WebhookSubscriptions.Should().BeEmpty();
    }

    [Fact]
    public async Task A_test_ping_is_sent_now_and_its_delivery_returned()
    {
        var subscription = AddSubscription();
        var bodies = new List<string>();
        var handler = new TestWebhookSubscriptionHandler(_db, Manual(HttpStatusCode.OK, bodies), _clock.Object);

        var delivery = await handler.Handle(new TestWebhookSubscriptionCommand(subscription.Id), CancellationToken.None);

        delivery.IsSuccess.Should().BeTrue();
        delivery.StatusCode.Should().Be(200);
        delivery.EventType.Should().Be(WebhookEventTypes.Ping);
        delivery.ResponseBody.Should().Be("received");
        JsonDocument.Parse(bodies.Single()).RootElement.GetProperty("event").GetString().Should().Be("webhook.ping");
        _db.IntegrationOutboxEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_test_ping_returns_the_failure_instead_of_throwing()
    {
        var subscription = AddSubscription();
        var handler = new TestWebhookSubscriptionHandler(_db, Manual(HttpStatusCode.BadGateway), _clock.Object);

        var delivery = await handler.Handle(new TestWebhookSubscriptionCommand(subscription.Id), CancellationToken.None);

        delivery.IsSuccess.Should().BeFalse();
        delivery.StatusCode.Should().Be(502);
        delivery.ErrorMessage.Should().Contain("502");
    }

    [Fact]
    public async Task An_inactive_subscription_cannot_be_tested()
    {
        var subscription = AddSubscription(active: false);
        var handler = new TestWebhookSubscriptionHandler(_db, Manual(HttpStatusCode.OK), _clock.Object);

        var act = () => handler.Handle(new TestWebhookSubscriptionCommand(subscription.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*inactive*");
    }

    [Fact]
    public async Task Redelivery_resends_the_original_body_as_a_new_attempt()
    {
        var subscription = AddSubscription();
        var original = new WebhookDelivery
        {
            SubscriptionId = subscription.Id,
            EventType = "invoice.created",
            PayloadJson = """{"id":"abc","event":"invoice.created","entityId":42}""",
            StatusCode = 500,
            AttemptedAt = _clock.Object.UtcNow.AddHours(-1),
        };
        _db.WebhookDeliveries.Add(original);
        _db.SaveChanges();
        var bodies = new List<string>();

        var delivery = await new RedeliverWebhookDeliveryHandler(_db, Manual(HttpStatusCode.OK, bodies))
            .Handle(new RedeliverWebhookDeliveryCommand(original.Id), CancellationToken.None);

        delivery.Id.Should().NotBe(original.Id);
        delivery.IsSuccess.Should().BeTrue();
        delivery.EventType.Should().Be("invoice.created");
        bodies.Single().Should().Be(original.PayloadJson);
        _db.WebhookDeliveries.Count().Should().Be(2);
    }

    [Fact]
    public async Task Redelivering_an_unknown_delivery_is_not_found()
    {
        var act = () => new RedeliverWebhookDeliveryHandler(_db, Manual(HttpStatusCode.OK))
            .Handle(new RedeliverWebhookDeliveryCommand(999), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    private sealed class RecordingHandler(HttpStatusCode status, List<string> bodies) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(status) { Content = new StringContent("received") };
        }
    }
}
