using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.RecurringOrders;

/// <summary>
/// Pauses or resumes a recurring order. An inactive template is skipped by the generation job.
/// Resuming a template whose next run date has passed moves that date forward by whole intervals
/// to the first run on or after today, so a long pause does not generate a burst of catch-up orders.
/// </summary>
public record SetRecurringOrderActiveCommand(int Id, bool IsActive) : IRequest<RecurringOrderDetailResponseModel>;

public class SetRecurringOrderActiveHandler(IRecurringOrderRepository repo, AppDbContext db, IClock clock, IMediator mediator)
    : IRequestHandler<SetRecurringOrderActiveCommand, RecurringOrderDetailResponseModel>
{
    public async Task<RecurringOrderDetailResponseModel> Handle(SetRecurringOrderActiveCommand request, CancellationToken cancellationToken)
    {
        var ro = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Recurring order {request.Id} not found");

        if (ro.IsActive != request.IsActive)
        {
            ro.IsActive = request.IsActive;
            var description = request.IsActive ? $"Recurring order {ro.Name} resumed" : $"Recurring order {ro.Name} paused";
            if (request.IsActive)
            {
                var today = clock.UtcNow.UtcDateTime.Date;
                var interval = Math.Max(1, ro.IntervalDays);
                var next = ro.NextGenerationDate;
                while (next.UtcDateTime.Date < today)
                    next = next.AddDays(interval);
                if (next != ro.NextGenerationDate)
                {
                    ro.NextGenerationDate = next;
                    description += $"; next run moved to {next:MM/dd/yyyy}";
                }
            }
            db.LogActivityAt(request.IsActive ? "activated" : "deactivated", description,
                ("RecurringOrder", ro.Id), ("Customer", ro.CustomerId));
            await repo.SaveChangesAsync(cancellationToken);
        }

        return await mediator.Send(new GetRecurringOrderByIdQuery(ro.Id), cancellationToken);
    }
}
