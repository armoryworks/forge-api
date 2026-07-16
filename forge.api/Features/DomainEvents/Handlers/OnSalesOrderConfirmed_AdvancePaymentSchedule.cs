using MediatR;

using Forge.Api.Features.PaymentSchedules;

namespace Forge.Api.Features.DomainEvents.Handlers;

/// <summary>
/// On SO confirmation, auto-generate invoices for any payment milestones that are now due
/// (OnAcceptance / OnOrderConfirmation, plus any already-elapsed FixedDate/NetDays). Delegates
/// to <see cref="AdvancePaymentScheduleCommand"/>, which is resilient per-milestone, so a
/// schedule issue never blocks confirmation.
/// </summary>
public class OnSalesOrderConfirmed_AdvancePaymentSchedule(IMediator mediator)
    : INotificationHandler<SalesOrderConfirmedEvent>
{
    public Task Handle(SalesOrderConfirmedEvent notification, CancellationToken ct)
        => mediator.Send(new AdvancePaymentScheduleCommand(notification.SalesOrderId), ct);
}
