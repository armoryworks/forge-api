using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.RetailBuyers;

/// <summary>
/// Scrubs personally-identifying columns from retail buyers whose retention
/// window has expired, or from one buyer on request.
///
/// <para><b>Scrub, not delete.</b> The row survives with its order links,
/// counts and dates intact — deleting it would orphan real orders and destroy
/// the repeat-buyer and channel-performance analytics that are the legitimate
/// reason to keep any of this. What goes is the part that identifies a person:
/// name, email, phone. The frozen <see cref="Entities.OrderShipTo"/> on each of
/// their orders is scrubbed alongside, since a delivery address identifies
/// someone just as well as their name does.</para>
///
/// <para>Driven by marketplace data-protection terms — Amazon's DPP is the
/// strictest and requires buyer PII to be deletable on request and not retained
/// past fulfilment need.</para>
/// </summary>
public record PurgeRetailBuyerPiiCommand(int? BuyerId = null) : IRequest<PurgeRetailBuyerPiiResult>;

public record PurgeRetailBuyerPiiResult(int BuyersPurged, int AddressesPurged);

public class PurgeRetailBuyerPiiHandler(AppDbContext db, IClock clock)
    : IRequestHandler<PurgeRetailBuyerPiiCommand, PurgeRetailBuyerPiiResult>
{
    /// <summary>Placeholder written over scrubbed identity columns so the UI shows an honest label rather than a blank.</summary>
    private const string RedactedName = "[purged]";

    /// <summary>Batch size for the scheduled sweep. Bounded per the efficiency rules — never load the whole table.</summary>
    private const int BatchSize = 500;

    public async Task<PurgeRetailBuyerPiiResult> Handle(PurgeRetailBuyerPiiCommand request, CancellationToken ct)
    {
        var now = clock.UtcNow;

        var query = db.RetailBuyers.Where(b => b.PurgedAt == null);

        query = request.BuyerId is int id
            // On-request erasure ignores the retention window — the whole point
            // is that the buyer asked before it elapsed.
            ? query.Where(b => b.Id == id)
            : query.Where(b => b.PurgeAfter != null && b.PurgeAfter <= now);

        var buyers = await query
            .OrderBy(b => b.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (buyers.Count == 0)
            return new PurgeRetailBuyerPiiResult(0, 0);

        var buyerIds = buyers.Select(b => b.Id).ToList();

        // One query for every affected ship-to, keyed back to its buyer through
        // the order — not a per-buyer lookup inside the loop.
        var shipTos = await db.OrderShipTos
            .Where(s => s.SalesOrder.RetailBuyerId != null && buyerIds.Contains(s.SalesOrder.RetailBuyerId!.Value))
            .ToListAsync(ct);

        foreach (var buyer in buyers)
        {
            buyer.DisplayName = RedactedName;
            buyer.ContactEmail = null;
            buyer.Phone = null;
            // Consent is meaningless once there is no one left to contact, and
            // leaving it true would let a later feature mail a redacted row.
            buyer.MarketingConsent = false;
            buyer.PurgedAt = now;

            db.LogActivityAt(
                "pii-purged",
                request.BuyerId.HasValue
                    ? "Buyer PII scrubbed on request. Order history and totals retained."
                    : "Buyer PII scrubbed — retention window elapsed. Order history and totals retained.",
                ("RetailBuyer", buyer.Id));
        }

        foreach (var shipTo in shipTos)
        {
            shipTo.Name = RedactedName;
            shipTo.Company = null;
            shipTo.Line1 = RedactedName;
            shipTo.Line2 = null;
            shipTo.Phone = null;
            // City / state / postal / country survive: they carry no personal
            // identity on their own and they are what regional sales and
            // shipping-cost analysis are computed from.
        }

        await db.SaveChangesAsync(ct);

        return new PurgeRetailBuyerPiiResult(buyers.Count, shipTos.Count);
    }
}
