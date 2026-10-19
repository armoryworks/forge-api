using MediatR;

using Forge.Api.Capabilities;
using Forge.Api.Features.Invoices;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

/// <summary>
/// CAP-O2C-COLLECTIONS — queues the 30 / 60 / 90-day payment reminder for an aging invoice. The past-due check
/// publishes every day for the highest bucket reached, and the reminder's outbox key de-dupes, so a customer
/// gets one reminder per bucket. Does nothing while the capability is off.
/// </summary>
public class OnInvoicePastDue_SendReminder(
    AppDbContext db,
    ICapabilitySnapshotProvider capabilities,
    IIntegrationOutboxService outbox)
    : INotificationHandler<InvoicePastDueEvent>
{
    public async Task Handle(InvoicePastDueEvent notification, CancellationToken ct)
    {
        if (!capabilities.IsEnabled(InvoiceDunning.Capability) || notification.DaysOverdue < 30)
            return;

        await InvoiceDunning.EnqueueReminderAsync(
            db, outbox, notification.InvoiceId, InvoiceDunning.BucketFor(notification.DaysOverdue), ct);
    }
}
