using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.PaymentSchedules;

/// <summary>
/// Advances a sales order's payment schedule: for every Pending milestone whose trigger is now
/// satisfied against the order's current state (deposit on confirmation, pre-production, on
/// shipment, on delivery, fixed date, net days — see <see cref="PaymentMilestoneEvaluator"/>),
/// auto-generates the milestone invoice. This is the "event plumbing" the milestone MVP left
/// out: it's fired on SO confirmation and can be re-run (idempotent — already-invoiced
/// milestones are skipped by <see cref="GenerateMilestoneInvoiceCommand"/>) as the order moves
/// through production/shipping. ⚡ Accounting-bounded via the underlying generate command.
/// </summary>
public record AdvancePaymentScheduleCommand(int SalesOrderId) : IRequest<List<InvoiceListItemModel>>;

public class AdvancePaymentScheduleHandler(
    AppDbContext db,
    IMediator mediator,
    IClock clock,
    ILogger<AdvancePaymentScheduleHandler> logger)
    : IRequestHandler<AdvancePaymentScheduleCommand, List<InvoiceListItemModel>>
{
    public async Task<List<InvoiceListItemModel>> Handle(
        AdvancePaymentScheduleCommand request, CancellationToken cancellationToken)
    {
        var schedule = await db.PaymentSchedules
            .Include(ps => ps.Milestones)
            .FirstOrDefaultAsync(ps => ps.SalesOrderId == request.SalesOrderId, cancellationToken);
        if (schedule is null)
            return [];

        var salesOrder = await db.SalesOrders
            .AsNoTracking()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == request.SalesOrderId, cancellationToken);
        if (salesOrder is null)
            return [];

        Quote? quote = schedule.QuoteId is int quoteId
            ? await db.Quotes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == quoteId, cancellationToken)
            : null;

        var now = clock.UtcNow;
        var due = schedule.Milestones
            .Where(m => m.Status == PaymentMilestoneStatus.Pending
                     && m.InvoiceId is null
                     && PaymentMilestoneEvaluator.IsTriggerSatisfied(m, quote, salesOrder, now))
            .OrderBy(m => m.Sequence)
            .ToList();

        var generated = new List<InvoiceListItemModel>();
        foreach (var milestone in due)
        {
            try
            {
                generated.Add(await mediator.Send(new GenerateMilestoneInvoiceCommand(milestone.Id), cancellationToken));
            }
            catch (Exception ex)
            {
                // A single milestone failing (e.g., provider connected, zero total) must not
                // break the caller (SO confirmation) or the remaining milestones.
                logger.LogWarning(ex,
                    "Auto-advance: could not generate invoice for milestone {MilestoneId} on sales order {SalesOrderId}",
                    milestone.Id, request.SalesOrderId);
            }
        }
        return generated;
    }
}
