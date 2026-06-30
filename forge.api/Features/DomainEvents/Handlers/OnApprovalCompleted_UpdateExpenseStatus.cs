using MediatR;
using Microsoft.Extensions.Logging;

using Forge.Api.Features.Expenses;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Features.DomainEvents.Handlers;

/// <summary>
/// F-26B-05 — keeps an expense's status in lock-step with its governing approval workflow.
/// When a governing <c>ApprovalRequest</c> reaches a terminal decision, this handler dispatches
/// the EXISTING <see cref="UpdateExpenseStatusCommand"/> so the vendor-bill promotion, AP/GL
/// posting (behind CAP-ACCT-FULLGL) and QBO sync side-effects on the Approved transition all run
/// unchanged — that load-bearing block is NOT duplicated here.
///
/// By the time this fires the request is already terminal (the approval handler published the
/// event AFTER persisting the terminal status), so the bypass guard in UpdateExpenseStatus —
/// which only trips on a NON-terminal {Pending, Escalated} request — does not block this sync.
/// </summary>
public class OnApprovalCompleted_UpdateExpenseStatus(
    IMediator mediator,
    ILogger<OnApprovalCompleted_UpdateExpenseStatus> logger)
    : INotificationHandler<ApprovalCompletedEvent>
{
    public async Task Handle(ApprovalCompletedEvent notification, CancellationToken ct)
    {
        // Only governs expenses — ignore every other polymorphic entity type.
        if (!string.Equals(notification.EntityType, "Expense", StringComparison.Ordinal))
            return;

        var status = notification.Approved ? ExpenseStatus.Approved : ExpenseStatus.Rejected;

        // Carry the terminal decision's actor (DecidedById) through as the approver so the
        // expense records who decided it, and so the command works without an HTTP context.
        await mediator.Send(
            new UpdateExpenseStatusCommand(
                notification.EntityId,
                new UpdateExpenseStatusRequestModel(status, notification.Notes),
                ActorUserId: notification.DecidedById),
            ct);

        logger.LogInformation(
            "Expense {ExpenseId} status synced to {Status} from completed approval workflow",
            notification.EntityId, status);
    }
}
