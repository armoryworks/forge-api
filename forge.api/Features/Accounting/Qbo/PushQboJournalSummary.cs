using System.Globalization;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting.Qbo;

/// <summary>
/// QB-001 — the one-way downstream push: compute the per-account period net
/// (the SAME aggregation as the Part-A journal-summary CSV, via
/// <see cref="ITrialBalanceService"/>), require a QBO mapping for EVERY account
/// with a nonzero net, build ONE balanced QuickBooks JournalEntry and deliver
/// it through <see cref="IQboJournalPushService"/>. Each push is recorded in
/// <see cref="QboExportLog"/>; re-pushing a range that overlaps an existing log
/// row is idempotent-by-warning — it requires <c>Force</c> (else 409 via
/// <see cref="InvalidOperationException"/>). QuickBooks is never the system of
/// record: nothing is read back. No per-entity activity log per GL-subsystem
/// precedent (system-level operation; the QboExportLog row IS the audit trail).
/// </summary>
[RequiresCapability("CAP-ACCT-QBO-EXPORT")]
public record PushQboJournalSummaryCommand(
    int BookId,
    DateOnly FromDate,
    DateOnly ToDate,
    bool Force = false) : IRequest<QboPushResultModel>;

public class PushQboJournalSummaryHandler(
    AppDbContext db,
    ITrialBalanceService trialBalanceService,
    IQboJournalPushService pushService,
    IClock clock) : IRequestHandler<PushQboJournalSummaryCommand, QboPushResultModel>
{
    public async Task<QboPushResultModel> Handle(
        PushQboJournalSummaryCommand request, CancellationToken cancellationToken)
    {
        if (request.ToDate < request.FromDate)
            throw new InvalidOperationException("The export range end date precedes its start date.");

        // Idempotent-by-warning: an overlapping prior push means the CPA may
        // already have these numbers — require an explicit force to re-send.
        if (!request.Force)
        {
            var overlap = await db.QboExportLogs.AsNoTracking()
                .Where(l => l.BookId == request.BookId
                    && l.FromDate <= request.ToDate
                    && l.ToDate >= request.FromDate)
                .OrderByDescending(l => l.PushedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (overlap is not null)
            {
                throw new InvalidOperationException(
                    $"A QuickBooks push already covers {overlap.FromDate:yyyy-MM-dd}..{overlap.ToDate:yyyy-MM-dd} " +
                    $"(QBO doc {overlap.QboDocId}, pushed {overlap.PushedAt:yyyy-MM-dd}). " +
                    "Re-pushing an overlapping range would double-book in QuickBooks — retry with force=true " +
                    "if the CPA has removed the prior entry.");
            }
        }

        // Same aggregation as the Part-A journal-summary CSV: per-account period
        // net, one-sided; zero-net accounts contribute nothing.
        var trialBalance = await trialBalanceService.GetTrialBalanceAsync(
            request.BookId, request.FromDate, request.ToDate, cancellationToken);

        var nonZeroRows = trialBalance.Rows.Where(r => r.NetBalance != 0m).ToList();
        if (nonZeroRows.Count == 0)
            throw new InvalidOperationException("Nothing to push — no account has a nonzero net for the period.");

        var accountIds = nonZeroRows.Select(r => r.GlAccountId).ToList();
        var maps = await db.QboAccountMaps.AsNoTracking()
            .Where(m => accountIds.Contains(m.GlAccountId))
            .ToDictionaryAsync(m => m.GlAccountId, cancellationToken);

        var unmapped = nonZeroRows
            .Where(r => !maps.ContainsKey(r.GlAccountId))
            .Select(r => r.AccountNumber)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        if (unmapped.Count > 0)
        {
            throw new InvalidOperationException(
                "Cannot push to QuickBooks — these GL accounts have activity but no QuickBooks mapping: " +
                $"{string.Join(", ", unmapped)}. Map them on the exports screen and retry.");
        }

        var memo = string.Create(
            CultureInfo.InvariantCulture,
            $"Forge GL summary {request.FromDate:yyyy-MM-dd}..{request.ToDate:yyyy-MM-dd}");

        var lines = nonZeroRows
            .Select(r => new QboJournalPushLine(
                maps[r.GlAccountId].QboAccountId,
                IsDebit: r.NetBalance > 0m,
                Amount: Math.Abs(r.NetBalance),
                Description: $"{r.AccountNumber} {r.AccountName}"))
            .ToList();

        var qboDocId = await pushService.PushJournalEntryAsync(
            new QboJournalEntryPush(request.ToDate, memo, lines), cancellationToken);

        var totalDebit = lines.Where(l => l.IsDebit).Sum(l => l.Amount);

        db.QboExportLogs.Add(new QboExportLog
        {
            BookId = request.BookId,
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            QboDocId = qboDocId,
            TotalDebit = totalDebit,
            PushedAt = clock.UtcNow,
            PushedBy = db.CurrentUserId,
        });
        await db.SaveChangesAsync(cancellationToken);

        return new QboPushResultModel(qboDocId, request.FromDate, request.ToDate, totalDebit, lines.Count);
    }
}
