using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.PaymentSchedules;

/// <summary>
/// Waives a milestone (it stops counting toward the collectible remainder).
/// Milestones with recorded money (Paid/PartiallyPaid) cannot be waived.
/// </summary>
public record WaiveMilestoneCommand(int MilestoneId) : IRequest;

public class WaiveMilestoneValidator : AbstractValidator<WaiveMilestoneCommand>
{
    public WaiveMilestoneValidator()
    {
        RuleFor(x => x.MilestoneId).GreaterThan(0);
    }
}

public class WaiveMilestoneHandler(AppDbContext db) : IRequestHandler<WaiveMilestoneCommand>
{
    public async Task Handle(WaiveMilestoneCommand request, CancellationToken cancellationToken)
    {
        var milestone = await db.PaymentMilestones
            .Include(m => m.PaymentSchedule)
            .FirstOrDefaultAsync(m => m.Id == request.MilestoneId, cancellationToken)
            ?? throw new KeyNotFoundException($"Payment milestone {request.MilestoneId} not found");

        if (milestone.Status is PaymentMilestoneStatus.Paid or PaymentMilestoneStatus.PartiallyPaid)
            throw new InvalidOperationException("Cannot waive a milestone that already has recorded payments");

        milestone.Status = PaymentMilestoneStatus.Waived;

        var schedule = milestone.PaymentSchedule;
        var indexingPoint = schedule.SalesOrderId is int soId
            ? ("SalesOrder", soId)
            : ("Quote", schedule.QuoteId!.Value);
        db.LogActivityAt(
            "payment-milestone-waived",
            $"Milestone {milestone.Sequence} '{milestone.Name}' ({milestone.Percentage:0.##}%) waived",
            indexingPoint);

        await db.SaveChangesAsync(cancellationToken);
    }
}
