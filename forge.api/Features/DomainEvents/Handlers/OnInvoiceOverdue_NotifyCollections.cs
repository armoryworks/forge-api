using MediatR;

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Invoices;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

/// <summary>
/// CAP-O2C-COLLECTIONS — when an invoice flips to Overdue, rings the bell for the office managers (admins when
/// there are none) and queues the customer's first payment reminder. Does nothing while the capability is off.
/// </summary>
public class OnInvoiceOverdue_NotifyCollections(
    AppDbContext db,
    ICapabilitySnapshotProvider capabilities,
    IIntegrationOutboxService outbox,
    IHubContext<NotificationHub> notificationHub,
    ILogger<OnInvoiceOverdue_NotifyCollections> logger,
    IClock clock)
    : INotificationHandler<InvoiceOverdueEvent>
{
    public async Task Handle(InvoiceOverdueEvent notification, CancellationToken ct)
    {
        if (!capabilities.IsEnabled(InvoiceDunning.Capability))
            return;

        var invoice = await db.Invoices
            .Include(i => i.Customer)
            .Include(i => i.Lines)
            .Include(i => i.PaymentApplications)
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == notification.InvoiceId, ct);
        if (invoice is null)
            return;

        var userIds = await RecipientsAsync("OfficeManager", ct);
        if (userIds.Count == 0)
            userIds = await RecipientsAsync("Admin", ct);

        var balance = InvoiceMoneyText.Format(invoice.BalanceDue, await InvoiceMoneyText.CurrencyAsync(db, invoice.CurrencyId, ct));
        foreach (var userId in userIds)
        {
            db.Notifications.Add(new Notification
            {
                UserId = userId,
                Type = "invoice_overdue",
                Severity = "warning",
                Source = "invoices",
                Title = "Invoice Overdue",
                Message = $"Invoice {invoice.InvoiceNumber} for {invoice.Customer?.Name ?? "Unknown"} is past due. Balance due: {balance}.",
                EntityType = "Invoice",
                EntityId = invoice.Id,
            });
        }
        await db.SaveChangesAsync(ct);

        foreach (var userId in userIds)
        {
            await notificationHub.Clients.Group($"user:{userId}")
                .SendAsync("notificationReceived", new { type = "invoice_overdue", invoiceId = invoice.Id }, ct);
        }

        var queued = await InvoiceDunning.EnqueueReminderAsync(db, outbox, invoice.Id, InvoiceDunning.BucketDue, clock.UtcNow, ct);
        logger.LogInformation(
            "Invoice {InvoiceNumber} overdue: notified {Count} users, reminder queued: {Queued}",
            invoice.InvoiceNumber, userIds.Count, queued);
    }

    private Task<List<int>> RecipientsAsync(string role, CancellationToken ct)
        => db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .Join(db.Users, x => x.UserId, u => u.Id, (x, u) => new { x.UserId, x.Name, u.IsActive })
            .Where(x => x.IsActive && x.Name == role)
            .Select(x => x.UserId)
            .Distinct()
            .OrderBy(id => id)
            .ToListAsync(ct);
}
