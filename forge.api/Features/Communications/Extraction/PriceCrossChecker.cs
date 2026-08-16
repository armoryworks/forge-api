using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Interfaces.Communications;
using Forge.Core.Models.Extraction;
using Forge.Data.Context;

namespace Forge.Api.Features.Communications.Extraction;

/// <inheritdoc cref="IPriceCrossChecker"/>
public class PriceCrossChecker(
    AppDbContext db,
    IPartPricingResolver pricing,
    ILogger<PriceCrossChecker> logger) : IPriceCrossChecker
{
    /// <summary>
    /// How far an extracted price may sit from the resolved one and still count
    /// as agreement. Two percent absorbs rounding and a stale-by-one-revision
    /// price list without absorbing a transposed digit, which is the error this
    /// is really guarding against — 15.00 vs 150.00 is 900% out, not 2%.
    /// </summary>
    private const decimal Tolerance = 0.02m;

    public async Task<PriceCrossCheck> CheckAsync(
        int customerId, int partId, decimal? extractedPrice, CancellationToken ct)
    {
        if (extractedPrice is not { } quoted)
            return PriceCrossCheck.NotApplicable("No unit price was extracted from the message.");

        if (quoted <= 0)
            return PriceCrossCheck.NotApplicable("Extracted unit price was zero or negative.");

        // Prefer what this customer has actually paid. A price list is what we
        // intend to charge; a shipped order line is what they agreed to, and on
        // a renegotiated part the two disagree for a while.
        var lastCharged = await db.SalesOrderLines
            .AsNoTracking()
            .Where(l => l.PartId == partId
                && l.UnitPrice > 0
                && l.SalesOrder.CustomerId == customerId
                && l.SalesOrder.Status != SalesOrderStatus.Cancelled)
            .OrderByDescending(l => l.SalesOrder.CreatedAt)
            .Select(l => (decimal?)l.UnitPrice)
            .FirstOrDefaultAsync(ct);

        decimal? expected = lastCharged;
        var basis = "the last price this customer was charged";

        if (expected is null)
        {
            var resolved = await pricing.ResolveAsync(partId, customerId, null, ct);
            if (resolved.UnitPrice > 0)
            {
                expected = resolved.UnitPrice;
                basis = $"the current {resolved.Source} price";
            }
        }

        if (expected is not { } baseline)
        {
            // No history and no list price. Common for a first order of a new
            // part, and explicitly not a failure — it just means this signal
            // cannot contribute, so the draft needs a human on other grounds.
            return PriceCrossCheck.NotApplicable(
                "No pricing history or list price exists for this customer and part, so the quoted "
                + "price could not be checked.");
        }

        var variance = (quoted - baseline) / baseline;

        if (Math.Abs(variance) <= Tolerance)
        {
            return new PriceCrossCheck(
                PriceCrossCheckOutcome.Match, quoted, baseline, variance,
                $"Quoted {quoted:C} matches {basis} ({baseline:C}).");
        }

        logger.LogInformation(
            "[PRICE-CHECK] Customer {CustomerId} part {PartId}: quoted {Quoted}, expected {Expected} ({Variance:P1})",
            customerId, partId, quoted, baseline, variance);

        return new PriceCrossCheck(
            PriceCrossCheckOutcome.Mismatch, quoted, baseline, variance,
            $"Quoted {quoted:C} is {variance:P1} from {basis} ({baseline:C}). "
            + "Confirm before approving — this is where a transposed digit shows up.");
    }
}
