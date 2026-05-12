using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public class CheckCapaEffectivenessJob(
    AppDbContext db,
    IClock clock,
    ILogger<CheckCapaEffectivenessJob> logger)
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var overdueCaps = await db.CorrectiveActions
            .Where(c => c.Status == CapaStatus.EffectivenessCheck)
            .Where(c => c.EffectivenessCheckDueDate.HasValue && c.EffectivenessCheckDueDate.Value <= now)
            .Where(c => !c.EffectivenessCheckDate.HasValue)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        foreach (var capa in overdueCaps)
        {
            logger.LogWarning(
                "CAPA {CapaNumber} effectiveness check is overdue (due {DueDate})",
                capa.CapaNumber, capa.EffectivenessCheckDueDate);
        }

        if (overdueCaps.Count > 0)
        {
            logger.LogInformation(
                "Found {Count} CAPAs with overdue effectiveness checks", overdueCaps.Count);
        }
    }
}
