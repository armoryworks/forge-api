using MediatR;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// QB-001 CPA export #3 — the "give the CPA one monthly JE" shape: per-account
/// period NET, one-sided (a net-debit account shows its net in the debit column
/// and 0.00 in credit, and vice versa). Accounts that net to zero over the
/// period are omitted — they contribute nothing to the summary JE. Because the
/// underlying trial balance balances, the emitted debit and credit columns total
/// equal — i.e. the file IS a balanced journal entry. Reuses
/// <see cref="ITrialBalanceService"/> for the aggregation; the QBO push (Part B)
/// uses the same shape so the file and the API push can never disagree.
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record ExportJournalSummaryCsvQuery(
    int BookId,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null)
    : IRequest<CsvExportResult>;

public class ExportJournalSummaryCsvHandler(ITrialBalanceService trialBalanceService)
    : IRequestHandler<ExportJournalSummaryCsvQuery, CsvExportResult>
{
    public async Task<CsvExportResult> Handle(ExportJournalSummaryCsvQuery request, CancellationToken cancellationToken)
    {
        var trialBalance = await trialBalanceService.GetTrialBalanceAsync(
            request.BookId, request.FromDate, request.ToDate, cancellationToken);

        var csv = new CsvBuilder()
            .AppendRow("accountNumber", "accountName", "totalDebit", "totalCredit");

        foreach (var row in trialBalance.Rows)
        {
            if (row.NetBalance == 0m) continue;

            csv.AppendRow(
                row.AccountNumber,
                row.AccountName,
                CsvBuilder.Amount(row.NetBalance > 0m ? row.NetBalance : 0m),
                CsvBuilder.Amount(row.NetBalance < 0m ? -row.NetBalance : 0m));
        }

        var fileName = $"journal-summary-{CsvBuilder.RangeSuffix(request.FromDate, request.ToDate)}.csv";
        return new CsvExportResult(csv.ToUtf8Bytes(), fileName);
    }
}
