using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.PaymentSchedules;

/// <summary>
/// One query for both lookups: by quote (schedule authoring view) or by sales
/// order (post-conversion view — same row, re-linked). Returns null when the
/// document has no live schedule; the controller maps that to 404.
/// </summary>
public record GetPaymentScheduleQuery(int? QuoteId, int? SalesOrderId) : IRequest<PaymentScheduleResponseModel?>;

public class GetPaymentScheduleValidator : AbstractValidator<GetPaymentScheduleQuery>
{
    public GetPaymentScheduleValidator()
    {
        RuleFor(x => x)
            .Must(x => (x.QuoteId is > 0) ^ (x.SalesOrderId is > 0))
            .WithMessage("Exactly one of quoteId or salesOrderId is required");
    }
}

public class GetPaymentScheduleHandler(AppDbContext db, IClock clock)
    : IRequestHandler<GetPaymentScheduleQuery, PaymentScheduleResponseModel?>
{
    public async Task<PaymentScheduleResponseModel?> Handle(GetPaymentScheduleQuery request, CancellationToken cancellationToken)
    {
        var schedules = db.PaymentSchedules.AsNoTracking().Include(s => s.Milestones);

        // Cancelled schedules are dead rows kept for audit — never surfaced here
        // (the partial unique index allows a replacement live schedule to exist).
        var schedule = request.QuoteId is int quoteId
            ? await schedules.FirstOrDefaultAsync(
                s => s.QuoteId == quoteId && s.Status != PaymentScheduleStatus.Cancelled, cancellationToken)
            : await schedules.FirstOrDefaultAsync(
                s => s.SalesOrderId == request.SalesOrderId && s.Status != PaymentScheduleStatus.Cancelled, cancellationToken);

        if (schedule is null)
            return null;

        // Load both linked documents (with lines — Total is computed from them):
        // the total comes from the SO once linked, and trigger evaluation needs
        // whichever documents exist (e.g. quote AcceptedDate anchors NetDays).
        var quote = schedule.QuoteId is int qId
            ? await db.Quotes.AsNoTracking().Include(q => q.Lines)
                .FirstOrDefaultAsync(q => q.Id == qId, cancellationToken)
            : null;
        var salesOrder = schedule.SalesOrderId is int soId
            ? await db.SalesOrders.AsNoTracking().Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == soId, cancellationToken)
            : null;

        return BuildResponse(schedule, quote, salesOrder, clock.UtcNow);
    }

    /// <summary>
    /// Derives the full read model: effective statuses, per-milestone amounts
    /// (locked amount wins over percentage × live total), and rollup totals.
    /// </summary>
    public static PaymentScheduleResponseModel BuildResponse(
        PaymentSchedule schedule, Quote? quote, SalesOrder? salesOrder, DateTimeOffset now)
    {
        // Amounts always derive from the live document total: the SO once linked
        // (it owns the commercial reality post-conversion), else the quote.
        var documentTotal = PaymentMilestoneEvaluator.QuantizeMoney(
            salesOrder?.Total ?? quote?.Total ?? 0m);

        var milestones = schedule.Milestones
            .OrderBy(m => m.Sequence)
            .Select(m => new PaymentMilestoneResponseModel(
                m.Id,
                m.Sequence,
                m.Name,
                m.Percentage,
                m.DueTrigger.ToString(),
                m.DueDate,
                m.NetDays,
                PaymentMilestoneEvaluator.EffectiveStatus(m, quote, salesOrder, now).ToString(),
                PaymentMilestoneEvaluator.DeriveAmount(m, documentTotal),
                m.PaidAmount ?? 0m,
                m.InvoiceId,
                m.Notes))
            .ToList();

        var paidTotal = milestones.Sum(m => m.PaidAmount);
        // Waived milestones drop out of what is still collectible; paid amounts
        // (including any overpayment) reduce the remainder.
        var remainingTotal = milestones
            .Where(m => m.Status != nameof(PaymentMilestoneStatus.Waived))
            .Sum(m => m.AmountDue) - paidTotal;

        return new PaymentScheduleResponseModel(
            schedule.Id,
            schedule.QuoteId,
            schedule.SalesOrderId,
            schedule.Status.ToString(),
            milestones,
            new PaymentScheduleTotalsModel(documentTotal, paidTotal, remainingTotal));
    }
}
