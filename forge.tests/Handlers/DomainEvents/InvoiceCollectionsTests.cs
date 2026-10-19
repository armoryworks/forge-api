using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using QuestPDF.Infrastructure;

using Forge.Api.Capabilities;
using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.DomainEvents.Handlers;
using Forge.Api.Hubs;
using Forge.Api.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.DomainEvents;

/// <summary>
/// CAP-O2C-COLLECTIONS: the Overdue flip rings the bell and queues the first reminder; the 30/60/90 buckets
/// each queue one reminder keyed <c>invoice-dunning:{id}:{bucket}</c>; with the capability off nothing happens.
/// A bucket reached more than the catch-up window ago, and a conversion opening-balance document, get no reminder.
/// </summary>
public class InvoiceCollectionsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IHubContext<NotificationHub>> _hub = new();
    private readonly Mock<IClientProxy> _proxy = new();
    private readonly List<string> _keys = [];
    private readonly Mock<IIntegrationOutboxService> _outbox = new();
    private readonly Mock<IClock> _clock = new();

    public InvoiceCollectionsTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(_proxy.Object);
        _hub.SetupGet(h => h.Clients).Returns(clients.Object);
        _outbox.Setup(o => o.EnqueueEmailAsync(
                Capture.In(_keys), It.IsAny<EmailMessage>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns((string key, EmailMessage _, string? _, int? _, CancellationToken _) =>
            {
                _db.IntegrationOutboxEntries.Add(new IntegrationOutboxEntry
                {
                    Provider = IntegrationProvider.Email, OperationKey = key, IdempotencyKey = key, Payload = "{}",
                });
                _db.SaveChanges();
                return Task.FromResult(new IntegrationOutboxEntry());
            });
    }

    public void Dispose() => _db.Dispose();

    private sealed class FakeCapabilities(bool collectionsOn) : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["CAP-O2C-COLLECTIONS"] = collectionsOn },
            DateTimeOffset.UtcNow);

        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private async Task<Invoice> SeedAsync(InvoiceStatus status = InvoiceStatus.Sent, int daysOverdue = 1, string? provider = null)
    {
        _db.SystemSettings.Add(new SystemSetting { Key = "company.name", Value = "Harbor Gear Works" });
        _db.Set<Currency>().Add(new Currency { Id = 1, Code = "USD", Name = "US Dollar", Symbol = "$" });
        _db.Roles.Add(new IdentityRole<int> { Id = 2, Name = "OfficeManager" });
        _db.Users.AddRange(new ApplicationUser { Id = 9, UserName = "om" }, new ApplicationUser { Id = 10, UserName = "gone", IsActive = false });
        _db.UserRoles.AddRange(new IdentityUserRole<int> { UserId = 9, RoleId = 2 }, new IdentityUserRole<int> { UserId = 10, RoleId = 2 });
        var customer = new Customer { Name = "Buyer Co", Email = "ap@buyer.test" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        var invoice = new Invoice
        {
            InvoiceNumber = "INV-6100", CustomerId = customer.Id, CurrencyId = 1, Status = status,
            InvoiceDate = Now.AddDays(-30 - daysOverdue), DueDate = Now.AddDays(-daysOverdue), Provider = provider,
            Lines = { new InvoiceLine { LineNumber = 1, Description = "Bracket", Quantity = 2, UnitPrice = 50m } },
        };
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();
        return invoice;
    }

    private OnInvoiceOverdue_NotifyCollections OverdueHandler(bool on)
        => new(_db, new FakeCapabilities(on), _outbox.Object, _hub.Object, NullLogger<OnInvoiceOverdue_NotifyCollections>.Instance, _clock.Object);

    [Fact]
    public async Task OverdueFlip_RingsTheBell_AndQueuesTheFirstReminder()
    {
        var invoice = await SeedAsync(InvoiceStatus.Overdue);

        await OverdueHandler(on: true).Handle(new InvoiceOverdueEvent(invoice.Id, invoice.CustomerId), CancellationToken.None);

        var bell = await _db.Notifications.SingleAsync();
        bell.UserId.Should().Be(9);
        bell.Type.Should().Be("invoice_overdue");
        bell.EntityId.Should().Be(invoice.Id);
        bell.Message.Should().Contain("$100.00");
        _proxy.Verify(p => p.SendCoreAsync("notificationReceived", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
        _keys.Should().Equal($"invoice-dunning:{invoice.Id}:due");
    }

    [Fact]
    public async Task CollectionsOff_DoesNothing()
    {
        var invoice = await SeedAsync(InvoiceStatus.Overdue);

        await OverdueHandler(on: false).Handle(new InvoiceOverdueEvent(invoice.Id, invoice.CustomerId), CancellationToken.None);
        await new OnInvoicePastDue_SendReminder(_db, new FakeCapabilities(false), _outbox.Object, _clock.Object)
            .Handle(new InvoicePastDueEvent(invoice.Id, invoice.CustomerId, 31), CancellationToken.None);

        (await _db.Notifications.AnyAsync()).Should().BeFalse();
        _keys.Should().BeEmpty();
    }

    [Fact]
    public async Task BucketReminders_AreOnePerBucket()
    {
        var invoice = await SeedAsync(InvoiceStatus.Overdue, daysOverdue: 0);
        var handler = new OnInvoicePastDue_SendReminder(_db, new FakeCapabilities(true), _outbox.Object, _clock.Object);

        foreach (var days in new[] { 30, 31, 45, 60, 75, 95, 96 })
        {
            _clock.SetupGet(c => c.UtcNow).Returns(Now.AddDays(days));
            await handler.Handle(new InvoicePastDueEvent(invoice.Id, invoice.CustomerId, days), CancellationToken.None);
        }

        _keys.Should().Equal(
            $"invoice-dunning:{invoice.Id}:30",
            $"invoice-dunning:{invoice.Id}:60",
            $"invoice-dunning:{invoice.Id}:90");
    }

    [Fact]
    public async Task BucketReachedLongAgo_GetsNoReminder()
    {
        var invoice = await SeedAsync(InvoiceStatus.Overdue, daysOverdue: 200);
        var handler = new OnInvoicePastDue_SendReminder(_db, new FakeCapabilities(true), _outbox.Object, _clock.Object);

        await handler.Handle(new InvoicePastDueEvent(invoice.Id, invoice.CustomerId, 200), CancellationToken.None);

        _keys.Should().BeEmpty();
    }

    [Fact]
    public async Task OverdueFlipLongPastTheDueDate_RingsTheBell_ButSendsNoReminder()
    {
        var invoice = await SeedAsync(InvoiceStatus.Overdue, daysOverdue: 19);

        await OverdueHandler(on: true).Handle(new InvoiceOverdueEvent(invoice.Id, invoice.CustomerId), CancellationToken.None);

        (await _db.Notifications.CountAsync()).Should().Be(1);
        _keys.Should().BeEmpty();
    }

    [Fact]
    public async Task ConversionOpeningBalance_GetsNoReminder()
    {
        var invoice = await SeedAsync(InvoiceStatus.Overdue, daysOverdue: 1, provider: "Conversion");
        var handler = new OnInvoicePastDue_SendReminder(_db, new FakeCapabilities(true), _outbox.Object, _clock.Object);

        await OverdueHandler(on: true).Handle(new InvoiceOverdueEvent(invoice.Id, invoice.CustomerId), CancellationToken.None);
        await handler.Handle(new InvoicePastDueEvent(invoice.Id, invoice.CustomerId, 31), CancellationToken.None);

        _keys.Should().BeEmpty();
    }

    [Fact]
    public async Task PaidInvoice_GetsNoReminder()
    {
        var invoice = await SeedAsync(InvoiceStatus.Paid);

        await new OnInvoicePastDue_SendReminder(_db, new FakeCapabilities(true), _outbox.Object, _clock.Object)
            .Handle(new InvoicePastDueEvent(invoice.Id, invoice.CustomerId, 31), CancellationToken.None);

        _keys.Should().BeEmpty();
    }

    [Fact]
    public async Task OverdueJob_PublishesTheFlip()
    {
        var invoice = await SeedAsync(InvoiceStatus.Sent);
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);
        var publisher = new Mock<IPublisher>();

        await new OverdueInvoiceJob(_db, clock.Object, NullLogger<OverdueInvoiceJob>.Instance, publisher.Object)
            .MarkOverdueInvoicesAsync(CancellationToken.None);

        (await _db.Invoices.SingleAsync()).Status.Should().Be(InvoiceStatus.Overdue);
        publisher.Verify(p => p.Publish(
            It.Is<InvoiceOverdueEvent>(e => e.InvoiceId == invoice.Id), It.IsAny<CancellationToken>()), Times.Once);
    }
}
