using System.Text.Json;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Notifications;
using Forge.Api.Features.Webhooks;
using Forge.Api.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Jobs;

public class OutboundChatAndWebhookDispatchTests
{
    private const int LenaId = 11;
    private const int OwenId = 12;

    private readonly Mock<IClock> _clock = new();

    public OutboundChatAndWebhookDispatchTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero));
    }

    private AppDbContext CreateDb(params string[] capabilities)
    {
        var interceptor = new NotificationChatFanOutInterceptor(
            new StubCapabilitySnapshotProvider(capabilities),
            _clock.Object,
            NullLogger<NotificationChatFanOutInterceptor>.Instance);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(interceptor)
            .Options;
        return new TestAppDbContext(options);
    }

    private static void SeedIntegrations(AppDbContext db)
    {
        db.UserIntegrations.AddRange(
            new UserIntegration { UserId = LenaId, Category = "messaging", ProviderId = "slack", EncryptedCredentials = "x" },
            new UserIntegration { UserId = LenaId, Category = "messaging", ProviderId = "smtp", EncryptedCredentials = "x" },
            new UserIntegration { UserId = LenaId, Category = "calendar", ProviderId = "google_calendar", EncryptedCredentials = "x" },
            new UserIntegration { UserId = OwenId, Category = "messaging", ProviderId = "teams", EncryptedCredentials = "x", IsActive = false });
        db.SaveChanges();
    }

    private IntegrationOutboxDispatcherJob Dispatcher(
        AppDbContext db, IWebhookDeliveryService webhooks, params IMessagingIntegrationService[] messaging) =>
        new(db, Mock.Of<IEmailService>(), _clock.Object, NullLogger<IntegrationOutboxDispatcherJob>.Instance, webhooks, messaging);

    [Fact]
    public async Task A_new_notification_queues_one_chat_entry_per_active_chat_integration_of_its_recipient()
    {
        using var db = CreateDb(NotificationChatFanOutInterceptor.CapabilityCode);
        SeedIntegrations(db);

        db.Notifications.AddRange(
            new Notification { UserId = LenaId, Title = "Mentioned", Message = "Marco mentioned you", Severity = "info", EntityType = "Job", EntityId = 3 },
            new Notification { UserId = OwenId, Title = "Mentioned", Message = "For Owen" });
        await db.SaveChangesAsync();

        var entry = db.IntegrationOutboxEntries.Should().ContainSingle().Subject;
        entry.Provider.Should().Be(IntegrationProvider.Messaging);
        entry.Status.Should().Be(OutboxStatus.Pending);
        entry.CreatedAt.Should().NotBe(default);
        var payload = JsonSerializer.Deserialize<ChatNotificationOutboxPayload>(entry.Payload)!;
        payload.UserId.Should().Be(LenaId);
        payload.ProviderId.Should().Be("slack");
        payload.Title.Should().Be("Mentioned");
        payload.EntityId.Should().Be(3);
    }

    [Fact]
    public async Task Nothing_is_queued_for_chat_while_the_chat_capability_is_off()
    {
        using var db = CreateDb();
        SeedIntegrations(db);

        db.Notifications.Add(new Notification { UserId = LenaId, Title = "Mentioned", Message = "Hi" });
        await db.SaveChangesAsync();

        db.IntegrationOutboxEntries.Should().BeEmpty();
        db.Notifications.Should().ContainSingle();
    }

    [Fact]
    public async Task The_dispatcher_sends_chat_entries_through_the_matching_messaging_provider()
    {
        using var db = CreateDb(NotificationChatFanOutInterceptor.CapabilityCode);
        SeedIntegrations(db);
        db.Notifications.Add(new Notification { UserId = LenaId, Title = "Low stock: RS-100", Message = "0 available", Severity = "warning" });
        await db.SaveChangesAsync();
        var slack = new Mock<IMessagingIntegrationService>();
        slack.SetupGet(s => s.ProviderId).Returns("slack");
        var teams = new Mock<IMessagingIntegrationService>();
        teams.SetupGet(s => s.ProviderId).Returns("teams");

        await Dispatcher(db, Mock.Of<IWebhookDeliveryService>(), teams.Object, slack.Object).DispatchPendingAsync();

        slack.Verify(s => s.SendNotificationAsync(LenaId, It.IsAny<int>(),
            It.Is<NotificationMessage>(m => m.Title == "Low stock: RS-100" && m.Severity == "warning"),
            It.IsAny<CancellationToken>()), Times.Once);
        teams.Verify(s => s.SendNotificationAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<NotificationMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        db.IntegrationOutboxEntries.Single().Status.Should().Be(OutboxStatus.Sent);
    }

    [Fact]
    public async Task With_a_single_registered_messaging_provider_chat_entries_use_it()
    {
        using var db = CreateDb(NotificationChatFanOutInterceptor.CapabilityCode);
        SeedIntegrations(db);
        db.Notifications.Add(new Notification { UserId = LenaId, Title = "Mentioned", Message = "Hi" });
        await db.SaveChangesAsync();
        var mock = new Mock<IMessagingIntegrationService>();
        mock.SetupGet(s => s.ProviderId).Returns("mock_messaging");

        await Dispatcher(db, Mock.Of<IWebhookDeliveryService>(), mock.Object).DispatchPendingAsync();

        mock.Verify(s => s.SendNotificationAsync(LenaId, It.IsAny<int>(), It.IsAny<NotificationMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        db.IntegrationOutboxEntries.Single().Status.Should().Be(OutboxStatus.Sent);
    }

    [Fact]
    public async Task A_failing_chat_send_is_left_failed_for_retry()
    {
        using var db = CreateDb(NotificationChatFanOutInterceptor.CapabilityCode);
        SeedIntegrations(db);
        db.Notifications.Add(new Notification { UserId = LenaId, Title = "Mentioned", Message = "Hi" });
        await db.SaveChangesAsync();
        var slack = new Mock<IMessagingIntegrationService>();
        slack.SetupGet(s => s.ProviderId).Returns("slack");
        slack.Setup(s => s.SendNotificationAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<NotificationMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("503"));

        await Dispatcher(db, Mock.Of<IWebhookDeliveryService>(), slack.Object).DispatchPendingAsync();

        var entry = db.IntegrationOutboxEntries.Single();
        entry.Status.Should().Be(OutboxStatus.Failed);
        entry.LastError.Should().Be("503");
    }

    [Fact]
    public async Task The_dispatcher_hands_webhook_entries_to_the_delivery_service()
    {
        using var db = CreateDb();
        db.IntegrationOutboxEntries.Add(new IntegrationOutboxEntry
        {
            Provider = IntegrationProvider.Webhook,
            OperationKey = "webhook:1:invoice.created:42",
            IdempotencyKey = "k",
            Payload = JsonSerializer.Serialize(new WebhookOutboxPayload(1, "invoice.created", Guid.NewGuid(), "{}")),
            NextAttemptAt = _clock.Object.UtcNow,
        });
        await db.SaveChangesAsync();
        var delivery = new Mock<IWebhookDeliveryService>();

        await Dispatcher(db, delivery.Object).DispatchPendingAsync();

        delivery.Verify(d => d.DeliverAsync(It.Is<IntegrationOutboxEntry>(e => e.OperationKey == "webhook:1:invoice.created:42"), It.IsAny<CancellationToken>()), Times.Once);
        db.IntegrationOutboxEntries.Single().Status.Should().Be(OutboxStatus.Sent);
    }
}
