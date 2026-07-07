using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.PaymentSchedules;

/// <summary>
/// PUT-style bulk replace of a quote's payment schedule: the request's milestone
/// list becomes THE schedule (existing milestones are soft-deleted and rebuilt).
///
/// <para><strong>Locked-money guard (documented rule):</strong> once ANY existing
/// milestone is Invoiced/PartiallyPaid/Paid, the whole upsert is rejected with a
/// clear message — the simplest safe rule. Diffing "is the locked milestone
/// preserved identically?" invites silent money drift; post-lock changes go
/// through the targeted milestone actions instead.</para>
/// </summary>
public record UpsertPaymentScheduleCommand(int QuoteId, List<PaymentMilestoneRequestModel> Milestones)
    : IRequest<PaymentScheduleResponseModel>;

public class UpsertPaymentScheduleValidator : AbstractValidator<UpsertPaymentScheduleCommand>
{
    /// <summary>Tolerance for the Σ percentage == 100 check (numeric(7,4) storage).</summary>
    private const decimal SumTolerance = 0.0001m;

    public UpsertPaymentScheduleValidator()
    {
        RuleFor(x => x.QuoteId).GreaterThan(0);
        RuleFor(x => x.Milestones)
            .NotEmpty().WithMessage("At least one milestone is required")
            .Must(m => m.Count <= 20).WithMessage("A payment schedule may have at most 20 milestones");
        RuleFor(x => x.Milestones)
            .Must(m => Math.Abs(m.Sum(x => x.Percentage) - 100m) <= SumTolerance)
            .WithMessage("Milestone percentages must sum to exactly 100")
            .When(x => x.Milestones is { Count: > 0 });
        RuleForEach(x => x.Milestones).ChildRules(milestone =>
        {
            milestone.RuleFor(m => m.Name).NotEmpty().MaximumLength(200);
            milestone.RuleFor(m => m.Percentage).GreaterThan(0m);
            milestone.RuleFor(m => m.DueDate)
                .NotNull().WithMessage("A FixedDate milestone requires a due date")
                .When(m => m.DueTrigger == PaymentDueTrigger.FixedDate);
            milestone.RuleFor(m => m.NetDays)
                .NotNull().WithMessage("A NetDays milestone requires a net-days value")
                .When(m => m.DueTrigger == PaymentDueTrigger.NetDays);
            milestone.RuleFor(m => m.NetDays).GreaterThan(0).When(m => m.NetDays is not null);
            milestone.RuleFor(m => m.Notes).MaximumLength(500);
        });
    }
}

public class UpsertPaymentScheduleHandler(AppDbContext db, IClock clock)
    : IRequestHandler<UpsertPaymentScheduleCommand, PaymentScheduleResponseModel>
{
    public async Task<PaymentScheduleResponseModel> Handle(UpsertPaymentScheduleCommand request, CancellationToken cancellationToken)
    {
        var quote = await db.Quotes.AsNoTracking().Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == request.QuoteId, cancellationToken)
            ?? throw new KeyNotFoundException($"Quote {request.QuoteId} not found");

        var schedule = await db.PaymentSchedules
            .Include(s => s.Milestones)
            .FirstOrDefaultAsync(
                s => s.QuoteId == request.QuoteId && s.Status != PaymentScheduleStatus.Cancelled,
                cancellationToken);

        if (schedule is null)
        {
            schedule = new PaymentSchedule { QuoteId = request.QuoteId, Status = PaymentScheduleStatus.Draft };

            // Late authoring: if the quote already converted, link + activate
            // immediately — the convert-time re-link has already run and won't
            // run again for this schedule.
            var salesOrderId = await db.SalesOrders
                .Where(o => o.QuoteId == request.QuoteId)
                .Select(o => (int?)o.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (salesOrderId is not null)
            {
                schedule.SalesOrderId = salesOrderId;
                schedule.Status = PaymentScheduleStatus.Active;
            }

            db.PaymentSchedules.Add(schedule);
        }
        else if (schedule.Milestones.Any(m => m.Status
                     is PaymentMilestoneStatus.Invoiced
                     or PaymentMilestoneStatus.PartiallyPaid
                     or PaymentMilestoneStatus.Paid))
        {
            // Locked-money guard — see the command doc-comment for the rationale.
            throw new InvalidOperationException(
                "The payment schedule has milestones that are already invoiced or paid and can no "
                + "longer be replaced. Use the milestone actions (mark-paid, waive) instead.");
        }

        // Bulk replace: soft-delete every existing milestone (DeletedBy is stamped
        // automatically), then rebuild from the request. Manual Waived overrides on
        // replaced milestones are intentionally discarded — the PUT body is the
        // complete new schedule definition.
        foreach (var existing in schedule.Milestones)
            existing.DeletedAt = clock.UtcNow;

        var replacements = request.Milestones
            .Select((m, index) => new PaymentMilestone
            {
                Sequence = index + 1,
                Name = m.Name,
                Percentage = m.Percentage,
                DueTrigger = m.DueTrigger,
                DueDate = m.DueTrigger == PaymentDueTrigger.FixedDate ? m.DueDate : null,
                NetDays = m.DueTrigger == PaymentDueTrigger.NetDays ? m.NetDays : null,
                Status = PaymentMilestoneStatus.Pending,
                Notes = m.Notes,
            })
            .ToList();
        foreach (var replacement in replacements)
            schedule.Milestones.Add(replacement);

        // One rollup row (schedule bridges Quote ↔ SalesOrder once linked).
        (string, int)[] indexingPoints = schedule.SalesOrderId is int soId
            ? [("Quote", request.QuoteId), ("SalesOrder", soId)]
            : [("Quote", request.QuoteId)];
        db.LogActivityAt(
            "payment-schedule-updated",
            $"Payment schedule updated: {replacements.Count} milestone(s) replacing the previous definition",
            indexingPoints);

        await db.SaveChangesAsync(cancellationToken);

        var salesOrder = schedule.SalesOrderId is int linkedSoId
            ? await db.SalesOrders.AsNoTracking().Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == linkedSoId, cancellationToken)
            : null;

        // Soft-deleted rows are still in the tracked collection — rebuild the
        // response from the live milestones only.
        schedule.Milestones = replacements;
        return GetPaymentScheduleHandler.BuildResponse(schedule, quote, salesOrder, clock.UtcNow);
    }
}
