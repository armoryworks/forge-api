using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <inheritdoc />
public sealed class FiscalPeriodCloseService(AppDbContext db) : IFiscalPeriodCloseService
{
    // Legal transitions. HardClosed is terminal (a hard-closed period is reopened only by an explicit
    // back-out at the DB level — never through this API). Open can hard-close directly (skipping soft).
    private static readonly Dictionary<FiscalPeriodStatus, FiscalPeriodStatus[]> Allowed = new()
    {
        [FiscalPeriodStatus.Open] = [FiscalPeriodStatus.SoftClosed, FiscalPeriodStatus.HardClosed],
        [FiscalPeriodStatus.SoftClosed] = [FiscalPeriodStatus.Open, FiscalPeriodStatus.HardClosed],
        [FiscalPeriodStatus.HardClosed] = [],
    };

    public async Task<FiscalPeriodModel> TransitionAsync(
        int periodId, FiscalPeriodStatus target, CancellationToken ct = default)
    {
        var period = await db.FiscalPeriods
            .FirstOrDefaultAsync(p => p.Id == periodId, ct)
            ?? throw new KeyNotFoundException($"Fiscal period {periodId} not found.");

        // Lock the period row so a concurrent post (which locks the same row in the engine) serializes with
        // this close, and reload to act on the committed status. Postgres only; a no-op elsewhere (InMemory).
        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlRawAsync(
                "SELECT id FROM acct_fiscal_periods WHERE id = {0} FOR UPDATE", [period.Id], ct);
            await db.Entry(period).ReloadAsync(ct);
        }

        if (period.Status == target)
            throw new InvalidOperationException($"Fiscal period {periodId} is already {target}.");

        if (!Allowed[period.Status].Contains(target))
            throw new InvalidOperationException(
                $"Cannot transition fiscal period {periodId} from {period.Status} to {target}.");

        period.Status = target;
        await db.SaveChangesAsync(ct); // Version token guards a concurrent status change

        return new FiscalPeriodModel(
            period.Id, period.FiscalYearId, period.PeriodNumber, period.Name,
            period.StartDate, period.EndDate, period.Status);
    }
}
