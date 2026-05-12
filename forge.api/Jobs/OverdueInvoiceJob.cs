using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

public class OverdueInvoiceJob(
    AppDbContext db,
    IClock clock,
    ILogger<OverdueInvoiceJob> logger)
{
    public async Task MarkOverdueInvoicesAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var overdueInvoices = await db.Invoices
            .Where(i => i.Status == InvoiceStatus.Sent && i.DueDate < now)
            .ToListAsync(ct);

        if (overdueInvoices.Count == 0)
        {
            logger.LogInformation("No invoices to mark as overdue");
            return;
        }

        foreach (var invoice in overdueInvoices)
        {
            invoice.Status = InvoiceStatus.Overdue;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Marked {Count} invoices as overdue", overdueInvoices.Count);
    }
}
