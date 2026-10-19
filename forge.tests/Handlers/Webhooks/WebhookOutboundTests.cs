using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.DomainEvents.Handlers;
using Forge.Api.Features.Webhooks;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Webhooks;

public class WebhookOutboundTests
{
    private const string Secret = "shared-secret";

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IClock> _clock = new();
    private readonly IDataProtectionProvider _dataProtection = new EphemeralDataProtectionProvider();

    public WebhookOutboundTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero));
    }

    private WebhookSubscription AddSubscription(string eventTypesJson, int maxRetries = 3, bool autoDisable = true, string? headersJson = null)
    {
        var subscription = new WebhookSubscription
        {
            Url = "http://hooks.example.test/in",
            EventTypesJson = eventTypesJson,
            EncryptedSecret = _dataProtection.CreateProtector(WebhookDeliveryService.SecretPurpose).Protect(Secret),
            MaxRetries = maxRetries,
            AutoDisableOnFailure = autoDisable,
            HeadersJson = headersJson,
        };
        _db.WebhookSubscriptions.Add(subscription);
        _db.SaveChanges();
        return subscription;
    }

    private WebhookEventPublisher Publisher(params string[] enabled) =>
        new(_db,
            new IntegrationOutboxService(_db, _clock.Object, NullLogger<IntegrationOutboxService>.Instance),
            new StubCapabilitySnapshotProvider(enabled),
            _clock.Object);

    private WebhookDeliveryService Delivery(HttpMessageHandler handler) =>
        new(_db, new StubHttpClientFactory(handler), _dataProtection, _clock.Object, NullLogger<WebhookDeliveryService>.Instance);

    [Theory]
    [InlineData("""["invoice.created"]""", true)]
    [InlineData("""["*"]""", true)]
    [InlineData("""["invoice.created","job.created"]""", true)]
    [InlineData("""["invoice.made"]""", false)]
    [InlineData("""[]""", false)]
    [InlineData("invoice.created", false)]
    public void Subscription_event_lists_must_name_known_events(string json, bool valid)
    {
        WebhookEventTypes.IsValidList(json).Should().Be(valid);
    }

    [Fact]
    public async Task Nothing_is_queued_while_outbound_webhooks_are_off()
    {
        AddSubscription("""["invoice.created"]""");

        var queued = await Publisher().PublishAsync(WebhookEventTypes.InvoiceCreated, "Invoice", 1, new { id = 1 });

        queued.Should().Be(0);
        _db.IntegrationOutboxEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task An_invoice_created_event_queues_one_entry_per_matching_active_subscription()
    {
        var invoiceSub = AddSubscription("""["invoice.created"]""", maxRetries: 3);
        AddSubscription("""["*"]""");
        AddSubscription("""["job.created"]""");
        var inactive = AddSubscription("""["invoice.created"]""");
        inactive.IsActive = false;
        await _db.SaveChangesAsync();
        var handler = new OnDomainEvent_EnqueueWebhooks(Publisher(WebhookEventPublisher.CapabilityCode), _clock.Object);

        await handler.Handle(new InvoiceCreatedEvent(42, "INV-42", 7, 85m), CancellationToken.None);
        await handler.Handle(new InvoiceCreatedEvent(42, "INV-42", 7, 85m), CancellationToken.None);

        var entries = _db.IntegrationOutboxEntries.ToList();
        entries.Should().HaveCount(2);
        entries.Should().OnlyContain(e => e.Provider == IntegrationProvider.Webhook && e.EntityType == "Invoice" && e.EntityId == 42);
        entries.Single(e => e.OperationKey.StartsWith($"webhook:{invoiceSub.Id}:")).MaxAttempts.Should().Be(4);

        var payload = JsonSerializer.Deserialize<WebhookOutboxPayload>(entries[0].Payload)!;
        using var body = JsonDocument.Parse(payload.Body);
        body.RootElement.GetProperty("event").GetString().Should().Be("invoice.created");
        body.RootElement.GetProperty("data").GetProperty("invoiceNumber").GetString().Should().Be("INV-42");
    }

    [Fact]
    public async Task A_successful_delivery_is_signed_and_recorded()
    {
        var subscription = AddSubscription("""["invoice.created"]""", headersJson: """{"X-Tenant":"plant-1","X-Forge-Signature":"spoof"}""");
        await Publisher(WebhookEventPublisher.CapabilityCode).PublishAsync(WebhookEventTypes.InvoiceCreated, "Invoice", 42, new { invoiceNumber = "INV-42" });
        var entry = _db.IntegrationOutboxEntries.Single();
        entry.AttemptCount = 1;
        var handler = new CapturingHandler(HttpStatusCode.OK);

        await Delivery(handler).DeliverAsync(entry, CancellationToken.None);

        var payload = JsonSerializer.Deserialize<WebhookOutboxPayload>(entry.Payload)!;
        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(payload.Body))).ToLowerInvariant();
        handler.Body.Should().Be(payload.Body);
        handler.Headers["X-Forge-Signature"].Should().Be($"sha256={expected}");
        handler.Headers["X-Forge-Event"].Should().Be("invoice.created");
        handler.Headers["X-Tenant"].Should().Be("plant-1");

        var delivery = _db.WebhookDeliveries.Single();
        delivery.IsSuccess.Should().BeTrue();
        delivery.StatusCode.Should().Be(200);
        delivery.AttemptNumber.Should().Be(1);
        _db.WebhookSubscriptions.Single(s => s.Id == subscription.Id).LastDeliveredAt.Should().NotBeNull();
    }

    [Fact]
    public async Task A_failed_delivery_is_recorded_and_rethrown_for_the_outbox_retry()
    {
        var subscription = AddSubscription("""["invoice.created"]""", maxRetries: 0, autoDisable: true);
        await Publisher(WebhookEventPublisher.CapabilityCode).PublishAsync(WebhookEventTypes.InvoiceCreated, "Invoice", 42, new { });
        var entry = _db.IntegrationOutboxEntries.Single();
        entry.AttemptCount = 1;

        var act = () => Delivery(new CapturingHandler(HttpStatusCode.InternalServerError)).DeliverAsync(entry, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        var delivery = _db.WebhookDeliveries.Single();
        delivery.IsSuccess.Should().BeFalse();
        delivery.StatusCode.Should().Be(500);
        var stored = _db.WebhookSubscriptions.Single(s => s.Id == subscription.Id);
        stored.FailureCount.Should().Be(1);
        stored.IsActive.Should().BeFalse();
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("fe80::1", true)]
    [InlineData("::ffff:127.0.0.1", true)]
    [InlineData("10.0.0.5", false)]
    [InlineData("172.18.0.4", false)]
    [InlineData("192.168.1.20", false)]
    [InlineData("93.184.216.34", false)]
    public void Loopback_and_link_local_targets_are_blocked(string address, bool blocked)
    {
        WebhookTargetGuard.IsBlocked(IPAddress.Parse(address)).Should().Be(blocked);
    }

    private sealed class CapturingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            foreach (var header in request.Headers)
                Headers[header.Key] = string.Join(",", header.Value);
            return new HttpResponseMessage(status) { Content = new StringContent("ok") };
        }
    }
}
