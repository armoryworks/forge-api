using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Phase-5 — the journal lines a Draft pay run will post (the same lines <c>PostPayRun</c> builds), each with
/// the account its determination key resolves to, so the poster sees the entry before confirming.
/// </summary>
[RequiresCapability("CAP-PAYROLL-RUN")]
public record GetPayRunJournalPreviewQuery(int PayRunId) : IRequest<IReadOnlyList<PayRunJournalPreviewLineModel>>;

public class GetPayRunJournalPreviewHandler(AppDbContext db)
    : IRequestHandler<GetPayRunJournalPreviewQuery, IReadOnlyList<PayRunJournalPreviewLineModel>>
{
    public async Task<IReadOnlyList<PayRunJournalPreviewLineModel>> Handle(
        GetPayRunJournalPreviewQuery request, CancellationToken ct)
    {
        var run = await db.PayRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.PayRunId, ct)
            ?? throw new KeyNotFoundException($"Pay run {request.PayRunId} not found.");

        var lines = PayrollService.BuildJournalLines(run);
        var keys = lines.Select(l => l.AccountKey!).Distinct().ToList();
        var accounts = await db.Set<AccountDeterminationRule>().AsNoTracking()
            .Where(r => r.BookId == run.BookId && keys.Contains(r.Key)
                && r.ItemId == null && r.CategoryId == null && r.ValuationClassId == null)
            .Select(r => new { r.Key, r.GlAccount.Id, r.GlAccount.AccountNumber, r.GlAccount.Name })
            .ToListAsync(ct);
        var byKey = accounts.GroupBy(a => a.Key).ToDictionary(g => g.Key, g => g.First());

        return lines.Select(l =>
        {
            byKey.TryGetValue(l.AccountKey!, out var account);
            return new PayRunJournalPreviewLineModel(
                l.AccountKey!, account?.Id, account?.AccountNumber, account?.Name, l.Debit, l.Credit, l.Description);
        }).ToList();
    }
}
