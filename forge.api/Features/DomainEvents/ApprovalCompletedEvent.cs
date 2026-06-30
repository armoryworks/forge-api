using MediatR;

namespace Forge.Api.Features.DomainEvents;

/// <summary>
/// F-26B-05 — raised when a governing <c>ApprovalRequest</c> reaches a TERMINAL
/// decision (final-step approval / auto-approval, or rejection). Published by the
/// forge.api approval handlers (ApproveRequest / RejectRequest) AFTER the request's
/// terminal status is persisted — never from the forge.data service (avoids the
/// layering inversion) and never on an intermediate step advance.
///
/// Reaction handlers translate the decision onto the underlying entity. For
/// <c>EntityType == "Expense"</c>, <see cref="DomainEvents.Handlers.OnApprovalCompleted_UpdateExpenseStatus"/>
/// dispatches the existing <c>UpdateExpenseStatusCommand</c> so the promotion /
/// AP / QBO side-effects run unchanged.
/// </summary>
/// <param name="EntityType">Polymorphic entity type the approval governs (e.g. "Expense").</param>
/// <param name="EntityId">The governed entity's id.</param>
/// <param name="Approved">True on a terminal approval, false on a rejection.</param>
/// <param name="DecidedById">The user who made the terminal decision (carried as the actor).</param>
/// <param name="Notes">The decision comments, if any.</param>
public record ApprovalCompletedEvent(
    string EntityType,
    int EntityId,
    bool Approved,
    int? DecidedById,
    string? Notes) : INotification;
