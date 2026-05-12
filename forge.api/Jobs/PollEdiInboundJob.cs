using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

public class PollEdiInboundJob(
    AppDbContext db,
    IEdiService ediService,
    ILogger<PollEdiInboundJob> logger)
{
    public async Task PollAllPartnersAsync(CancellationToken ct = default)
    {
        var activePartners = await db.EdiTradingPartners
            .AsNoTracking()
            .Where(p => p.IsActive && p.AutoProcess)
            .ToListAsync(ct);

        if (activePartners.Count == 0)
        {
            logger.LogDebug("No active EDI trading partners to poll");
            return;
        }

        foreach (var partner in activePartners)
        {
            try
            {
                var transactions = await ediService.PollInboundAsync(partner.Id, ct);
                if (transactions.Count > 0)
                {
                    logger.LogInformation("EDI poll for {Partner}: received {Count} document(s)",
                        partner.Name, transactions.Count);

                    foreach (var transaction in transactions)
                    {
                        db.EdiTransactions.Add(transaction);
                    }

                    await db.SaveChangesAsync(ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "EDI poll failed for partner {Partner} ({PartnerId})",
                    partner.Name, partner.Id);
            }
        }
    }
}
