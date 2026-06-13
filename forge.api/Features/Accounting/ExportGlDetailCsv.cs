using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Enums.Accounting;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// QB-001 CPA export #2 — full GL detail as a CSV file: one row per journal LINE
/// for the book over the (inclusive) date range, ordered by entry number then
/// line number. Posted AND Reversed headers are both included — same rationale
/// as <see cref="TrialBalanceService"/>: a Reversed original plus its Posted
/// reversal net to zero, and the CPA needs to see both rows to follow the audit
/// trail. Built as a single AsNoTracking join query (no N+1).
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record ExportGlDetailCsvQuery(
    int BookId,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null)
    : IRequest<CsvExportResult>;

public class ExportGlDetailCsvHandler(AppDbContext db)
    : IRequestHandler<ExportGlDetailCsvQuery, CsvExportResult>
{
    public async Task<CsvExportResult> Handle(ExportGlDetailCsvQuery request, CancellationToken cancellationToken)
    {
        // Single joined projection — lines × entries × accounts — streamed into the
        // builder. IgnoreQueryFilters mirrors TrialBalanceService (filter-immune:
        // the ledger must never silently drop a row).
        var rows = await (
            from line in db.JournalLines.AsNoTracking().IgnoreQueryFilters()
            join entry in db.JournalEntries.AsNoTracking().IgnoreQueryFilters()
                on line.JournalEntryId equals entry.Id
            where entry.BookId == request.BookId
                && (entry.Status == JournalEntryStatus.Posted
                    || entry.Status == JournalEntryStatus.Reversed)
                && (request.FromDate == null || entry.EntryDate >= request.FromDate)
                && (request.ToDate == null || entry.EntryDate <= request.ToDate)
            join account in db.GlAccounts.AsNoTracking().IgnoreQueryFilters()
                on line.GlAccountId equals account.Id
            orderby entry.EntryNumber, line.LineNumber
            select new
            {
                entry.EntryNumber,
                entry.EntryDate,
                entry.Source,
                entry.SourceType,
                entry.SourceId,
                entry.Memo,
                account.AccountNumber,
                AccountName = account.Name,
                line.Debit,
                line.Credit,
                line.JobId,
                line.CostCenterId,
            })
            .ToListAsync(cancellationToken);

        var csv = new CsvBuilder()
            .AppendRow(
                "entryNumber", "entryDate", "source", "sourceRef", "memo",
                "accountNumber", "accountName", "debit", "credit", "jobId", "costCenterId");

        foreach (var row in rows)
        {
            var sourceRef = row.SourceType is not null && row.SourceId is not null
                ? $"{row.SourceType}:{row.SourceId}"
                : string.Empty;

            csv.AppendRow(
                row.EntryNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CsvBuilder.Date(row.EntryDate),
                row.Source.ToString(),
                sourceRef,
                row.Memo,
                row.AccountNumber,
                row.AccountName,
                CsvBuilder.Amount(row.Debit),
                CsvBuilder.Amount(row.Credit),
                row.JobId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                row.CostCenterId?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var fileName = $"gl-detail-{CsvBuilder.RangeSuffix(request.FromDate, request.ToDate)}.csv";
        return new CsvExportResult(csv.ToUtf8Bytes(), fileName);
    }
}
