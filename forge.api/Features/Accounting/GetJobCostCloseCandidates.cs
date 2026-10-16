using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Period-close worklist for <see cref="CloseJobProductionCostCommand"/>: jobs whose GL WIP balance in the
/// active book is not zero and that have no production-variance close entry yet (the close's own
/// idempotency key), completed jobs first. Read only.
/// </summary>
public record GetJobCostCloseCandidatesQuery : IRequest<IReadOnlyList<JobCostCloseCandidateModel>>;

public class GetJobCostCloseCandidatesHandler(AppDbContext db)
    : IRequestHandler<GetJobCostCloseCandidatesQuery, IReadOnlyList<JobCostCloseCandidateModel>>
{
    private const string KeyInventoryWip = "INVENTORY_WIP";

    public async Task<IReadOnlyList<JobCostCloseCandidateModel>> Handle(
        GetJobCostCloseCandidatesQuery request, CancellationToken ct)
    {
        var bookId = await db.Books.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Id)
            .Select(b => (int?)b.Id)
            .FirstOrDefaultAsync(ct);
        if (bookId is null)
            return [];

        var wipAccountId = await db.Set<AccountDeterminationRule>()
            .AsNoTracking()
            .Where(r => r.BookId == bookId && r.Key == KeyInventoryWip)
            .Select(r => (int?)r.GlAccountId)
            .FirstOrDefaultAsync(ct);
        if (wipAccountId is null)
            return [];

        var balances = await
            (from line in db.JournalLines.IgnoreQueryFilters()
             join je in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals je.Id
             where je.BookId == bookId
                 && line.GlAccountId == wipAccountId.Value
                 && line.JobId != null
                 && (je.Status == JournalEntryStatus.Posted || je.Status == JournalEntryStatus.Reversed)
             group line by line.JobId into g
             select new { JobId = g.Key!.Value, Balance = g.Sum(l => l.Debit - l.Credit) })
            .ToListAsync(ct);
        balances = balances.Where(b => Math.Round(b.Balance, 2) != 0m).ToList();
        if (balances.Count == 0)
            return [];

        var jobIds = balances.Select(b => b.JobId).ToList();
        var closedKeys = jobIds.Select(id => $"{JournalSource.Inventory}:Job:{id}:PRODVARIANCE").ToList();
        var closedJobKeys = await db.JournalEntries.IgnoreQueryFilters()
            .Where(e => e.BookId == bookId && e.IdempotencyKey != null && closedKeys.Contains(e.IdempotencyKey))
            .Select(e => e.IdempotencyKey!)
            .ToListAsync(ct);
        var closed = closedJobKeys.ToHashSet(StringComparer.Ordinal);

        var jobs = await db.Jobs.AsNoTracking()
            .Where(j => jobIds.Contains(j.Id))
            .Select(j => new { j.Id, j.JobNumber, j.Title, j.CompletedDate })
            .ToListAsync(ct);
        var balanceByJob = balances.ToDictionary(b => b.JobId, b => b.Balance);

        return jobs
            .Where(j => !closed.Contains($"{JournalSource.Inventory}:Job:{j.Id}:PRODVARIANCE"))
            .Select(j => new JobCostCloseCandidateModel(
                j.Id, j.JobNumber, j.Title, j.CompletedDate, Math.Round(balanceByJob[j.Id], 2)))
            .OrderBy(j => j.CompletedDate is null)
            .ThenBy(j => j.CompletedDate)
            .ThenBy(j => j.JobNumber, StringComparer.Ordinal)
            .ToList();
    }
}
