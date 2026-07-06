using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Read-only GL register for a book — the time-ordered journal with per-line account labels and
/// drill-back refs, feeding the §5A ledger-view UI. DARK behind <c>CAP-ACCT-FULLGL</c>. Newest first,
/// paginated (default 25 / max 100), optionally filtered by date range, entry status, and account.
/// This is a read seam only: it never touches <c>IPostingEngine</c> and never writes.
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record GetLedgerRegisterQuery(
    int BookId,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    JournalEntryStatus? Status = null,
    int? GlAccountId = null,
    int Page = 1,
    int PageSize = 25)
    : IRequest<LedgerRegisterPage>;

public class GetLedgerRegisterHandler(AppDbContext db)
    : IRequestHandler<GetLedgerRegisterQuery, LedgerRegisterPage>
{
    private const int MaxPageSize = 100;

    public async Task<LedgerRegisterPage> Handle(GetLedgerRegisterQuery request, CancellationToken ct)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);

        var q = db.JournalEntries
            .AsNoTracking()
            .Where(e => e.BookId == request.BookId);

        if (request.FromDate is { } from) q = q.Where(e => e.EntryDate >= from);
        if (request.ToDate is { } to) q = q.Where(e => e.EntryDate <= to);
        if (request.Status is { } status) q = q.Where(e => e.Status == status);
        if (request.GlAccountId is { } accountId) q = q.Where(e => e.Lines.Any(l => l.GlAccountId == accountId));

        var totalCount = await q.CountAsync(ct);

        var entries = await q
            .Include(e => e.Lines)
            .OrderByDescending(e => e.EntryDate)
            .ThenByDescending(e => e.EntryNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // Resolve account number/name once, batched — IgnoreQueryFilters so a later-deactivated
        // (soft-deleted) account still labels its historical lines instead of dropping to "(unknown)".
        var accountIds = entries.SelectMany(e => e.Lines).Select(l => l.GlAccountId).Distinct().ToList();
        var accountById = (await db.GlAccounts
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(a => accountIds.Contains(a.Id))
                .Select(a => new { a.Id, a.AccountNumber, a.Name })
                .ToListAsync(ct))
            .ToDictionary(a => a.Id, a => (a.AccountNumber, a.Name));

        var data = entries.Select(e => new LedgerRegisterEntry(
                e.Id,
                e.EntryNumber,
                e.EntryDate,
                e.Source.ToString(),
                e.SourceType,
                e.SourceId,
                e.Status.ToString(),
                e.Memo,
                e.ReversalOfEntryId,
                e.ReversedByEntryId,
                e.PostedAt,
                e.Lines
                    .OrderBy(l => l.LineNumber)
                    .Select(l =>
                    {
                        var number = "(unknown)";
                        var name = "(unknown)";
                        if (accountById.TryGetValue(l.GlAccountId, out var acct))
                        {
                            number = acct.AccountNumber;
                            name = acct.Name;
                        }

                        return new LedgerRegisterLine(
                            l.Id, l.LineNumber, l.GlAccountId, number, name,
                            l.Debit, l.Credit, l.Description, l.JobId, l.CostCenterId);
                    })
                    .ToList()))
            .ToList();

        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
        return new LedgerRegisterPage(data, page, pageSize, totalCount, totalPages);
    }
}

/// <summary>One journal entry in the register, with the drill-back refs the ledger UI navigates by.</summary>
public record LedgerRegisterEntry(
    long Id,
    long EntryNumber,
    DateOnly EntryDate,
    string Source,
    string? SourceType,
    long? SourceId,
    string Status,
    string? Memo,
    long? ReversalOfEntryId,
    long? ReversedByEntryId,
    DateTimeOffset? PostedAt,
    IReadOnlyList<LedgerRegisterLine> Lines);

/// <summary>One posting line, with its account labelled and debit/credit kept as separate non-negative amounts.</summary>
public record LedgerRegisterLine(
    long Id,
    int LineNumber,
    int GlAccountId,
    string AccountNumber,
    string AccountName,
    decimal Debit,
    decimal Credit,
    string? Description,
    int? JobId,
    int? CostCenterId);

/// <summary>Offset-paginated register page (matches the app-wide <c>{ data, page, pageSize, totalCount, totalPages }</c>).</summary>
public record LedgerRegisterPage(
    IReadOnlyList<LedgerRegisterEntry> Data,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
