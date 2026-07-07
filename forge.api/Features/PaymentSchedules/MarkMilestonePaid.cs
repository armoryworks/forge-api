using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.PaymentSchedules;

/// <summary>
/// Records a (possibly partial) payment against a milestone. Payments
/// accumulate; the first payment locks the derived amount (AmountLocked) so
/// later document edits can't retroactively change a collected deposit.
/// </summary>
public record MarkMilestonePaidCommand(int MilestoneId, decimal PaidAmount, string? PaidReference)
    : IRequest<PaymentMilestoneResponseModel>;

public class MarkMilestonePaidValidator : AbstractValidator<MarkMilestonePaidCommand>
{
    public MarkMilestonePaidValidator()
    {
        RuleFor(x => x.MilestoneId).GreaterThan(0);
        RuleFor(x => x.PaidAmount).GreaterThan(0m);
        RuleFor(x => x.PaidReference).MaximumLength(200);
    }
}

public class MarkMilestonePaidHandler(AppDbContext db, IClock clock)
    : IRequestHandler<MarkMilestonePaidCommand, PaymentMilestoneResponseModel>
{
    public async Task<PaymentMilestoneResponseModel> Handle(MarkMilestonePaidCommand request, CancellationToken cancellationToken)
    {
        var milestone = await db.PaymentMilestones
            .Include(m => m.PaymentSchedule)
            .FirstOrDefaultAsync(m => m.Id == request.MilestoneId, cancellationToken)
            ?? throw new KeyNotFoundException($"Payment milestone {request.MilestoneId} not found");

        if (milestone.Status == PaymentMilestoneStatus.Waived)
            throw new InvalidOperationException("Cannot record a payment against a waived milestone");
        if (milestone.Status == PaymentMilestoneStatus.Paid)
            throw new InvalidOperationException("Milestone is already fully paid");

        var schedule = milestone.PaymentSchedule;
        var quote = schedule.QuoteId is int quoteId
            ? await db.Quotes.AsNoTracking().Include(q => q.Lines)
                .FirstOrDefaultAsync(q => q.Id == quoteId, cancellationToken)
            : null;
        var salesOrder = schedule.SalesOrderId is int soId
            ? await db.SalesOrders.AsNoTracking().Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == soId, cancellationToken)
            : null;

        var documentTotal = PaymentMilestoneEvaluator.QuantizeMoney(salesOrder?.Total ?? quote?.Total ?? 0m);
        var amountDue = PaymentMilestoneEvaluator.DeriveAmount(milestone, documentTotal);

        // First payment freezes the amount: from here on the milestone's worth is
        // this figure, regardless of later document edits.
        milestone.AmountLocked ??= amountDue;

        milestone.PaidAmount = (milestone.PaidAmount ?? 0m) + request.PaidAmount;
        milestone.PaidAt = clock.UtcNow;
        if (request.PaidReference is not null)
            milestone.PaidReference = request.PaidReference;

        milestone.Status = PaymentMilestoneEvaluator.QuantizeMoney(milestone.PaidAmount.Value) >= amountDue
            ? PaymentMilestoneStatus.Paid
            : PaymentMilestoneStatus.PartiallyPaid;

        // Transactional event — log on the schedule's document (SO once linked).
        var indexingPoint = schedule.SalesOrderId is int linkedSoId
            ? ("SalesOrder", linkedSoId)
            : ("Quote", schedule.QuoteId!.Value);
        db.LogActivityAt(
            "payment-milestone-paid",
            $"Payment of {request.PaidAmount:0.00} recorded on milestone {milestone.Sequence} "
            + $"'{milestone.Name}' — {milestone.Status}",
            indexingPoint);

        await db.SaveChangesAsync(cancellationToken);

        return new PaymentMilestoneResponseModel(
            milestone.Id, milestone.Sequence, milestone.Name, milestone.Percentage,
            milestone.DueTrigger.ToString(), milestone.DueDate, milestone.NetDays,
            milestone.Status.ToString(), amountDue, milestone.PaidAmount.Value,
            milestone.InvoiceId, milestone.Notes);
    }
}
