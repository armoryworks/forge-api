using FluentValidation;
using Hangfire;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Jobs;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.PaymentTransmissions;

/// <summary>
/// Manually re-queues a Failed (or Cancelled) transmission for processing. AttemptCount is RESET to 0
/// so the manual reprocess gets a fresh 5-attempt cycle — the prior cycle's history stays on the
/// activity log; LastError is kept for context until the next attempt overwrites it.
/// </summary>
public record RetryPaymentTransmissionCommand(int Id) : IRequest<PaymentTransmissionListItemModel>;

public class RetryPaymentTransmissionValidator : AbstractValidator<RetryPaymentTransmissionCommand>
{
    public RetryPaymentTransmissionValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
    }
}

public class RetryPaymentTransmissionHandler(
    AppDbContext db,
    // Optional / null-default (mirrors the CreateVendorPayment seam) so unit tests without Hangfire
    // storage can exercise the state transition; only the enqueue is skipped when null.
    IBackgroundJobClient? backgroundJobs = null)
    : IRequestHandler<RetryPaymentTransmissionCommand, PaymentTransmissionListItemModel>
{
    public async Task<PaymentTransmissionListItemModel> Handle(
        RetryPaymentTransmissionCommand request, CancellationToken cancellationToken)
    {
        var transmission = await db.PaymentTransmissions
            .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Payment transmission {request.Id} not found");

        if (transmission.Status is not (PaymentTransmissionStatus.Failed or PaymentTransmissionStatus.Cancelled))
            throw new InvalidOperationException(
                $"Payment transmission {transmission.Id} is {transmission.Status}; "
                + "only Failed or Cancelled transmissions can be retried");

        // A voided (soft-deleted) payment must never move money — re-queueing its transmission would
        // resubmit a disbursement whose GL/operational record has been reversed.
        if (transmission.SourceType == "VendorPayment")
        {
            var voided = await db.VendorPayments.IgnoreQueryFilters()
                .AnyAsync(p => p.Id == transmission.SourceId && p.DeletedAt != null, cancellationToken);
            if (voided)
                throw new InvalidOperationException(
                    $"Payment transmission {transmission.Id} cannot be retried: the payment has been voided");
        }

        transmission.Status = PaymentTransmissionStatus.Queued;
        transmission.AttemptCount = 0;
        transmission.NextAttemptAt = null;
        // LastError intentionally kept — visible in triage until the next attempt overwrites it.

        db.LogActivityAt(
            "transmission-retried",
            $"Bank transmission manually re-queued — {transmission.Amount:C} via {transmission.Method}",
            (transmission.SourceType, transmission.SourceId));
        await db.SaveChangesAsync(cancellationToken);

        backgroundJobs?.Enqueue<PaymentTransmissionJob>(
            j => j.ProcessAsync(transmission.Id, CancellationToken.None));

        return new PaymentTransmissionListItemModel(
            transmission.Id, transmission.SourceType, transmission.SourceId, transmission.Status.ToString(),
            transmission.AttemptCount, PaymentTransmissionJob.MaxAttempts,
            transmission.LastAttemptAt, transmission.NextAttemptAt, transmission.LastError,
            transmission.SubmissionRef, transmission.Amount, transmission.Method, transmission.CreatedAt);
    }
}
