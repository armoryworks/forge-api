using MediatR;

using Forge.Api.Features.RetailBuyers;

namespace Forge.Api.Jobs;

/// <summary>
/// Scheduled sweep that scrubs retail-buyer PII once its retention window has
/// elapsed. Keeping this on a timer rather than relying on ad-hoc requests is
/// what makes the retention promise real — marketplace data-protection terms
/// bound how long buyer data may be held, not merely how it responds to an
/// erasure request.
///
/// <para>Each pass handles a bounded batch and loops until a pass finds nothing
/// left, so a long-idle install with a large backlog drains without loading the
/// table into memory.</para>
/// </summary>
public class RetailBuyerPurgeJob(IMediator mediator, ILogger<RetailBuyerPurgeJob> logger)
{
    /// <summary>Guards against an unbounded loop if a row somehow never clears; 200 batches is 100k buyers.</summary>
    private const int MaxBatchesPerRun = 200;

    public async Task PurgeExpiredAsync(CancellationToken ct = default)
    {
        var totalBuyers = 0;
        var totalAddresses = 0;
        var batches = 0;

        while (batches < MaxBatchesPerRun)
        {
            ct.ThrowIfCancellationRequested();

            var result = await mediator.Send(new PurgeRetailBuyerPiiCommand(), ct);
            if (result.BuyersPurged == 0)
                break;

            totalBuyers += result.BuyersPurged;
            totalAddresses += result.AddressesPurged;
            batches++;
        }

        if (batches >= MaxBatchesPerRun)
        {
            logger.LogWarning(
                "[RETAIL-PURGE] Hit the {Max}-batch cap with work still pending — the next run continues. " +
                "If this recurs, the retention window is likely set far in the past on a large backlog.",
                MaxBatchesPerRun);
        }

        if (totalBuyers > 0)
        {
            logger.LogInformation(
                "[RETAIL-PURGE] Scrubbed PII for {Buyers} retail buyer(s) and {Addresses} ship-to address(es)",
                totalBuyers, totalAddresses);
        }
    }
}
