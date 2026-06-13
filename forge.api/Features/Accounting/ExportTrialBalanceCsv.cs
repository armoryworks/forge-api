using MediatR;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// QB-001 CPA export #1 — the trial balance as a CSV file (account number, name,
/// debit, credit, net). Reuses <see cref="ITrialBalanceService"/> (the same
/// filter-immune Posted+Reversed projection the on-screen report uses) and only
/// adds the CSV rendering, so the file can never disagree with the screen.
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record ExportTrialBalanceCsvQuery(
    int BookId,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null)
    : IRequest<CsvExportResult>;

public class ExportTrialBalanceCsvHandler(ITrialBalanceService trialBalanceService)
    : IRequestHandler<ExportTrialBalanceCsvQuery, CsvExportResult>
{
    public async Task<CsvExportResult> Handle(ExportTrialBalanceCsvQuery request, CancellationToken cancellationToken)
    {
        var trialBalance = await trialBalanceService.GetTrialBalanceAsync(
            request.BookId, request.FromDate, request.ToDate, cancellationToken);

        var csv = new CsvBuilder()
            .AppendRow("accountNumber", "accountName", "debit", "credit", "net");

        foreach (var row in trialBalance.Rows)
        {
            csv.AppendRow(
                row.AccountNumber,
                row.AccountName,
                CsvBuilder.Amount(row.DebitTotal),
                CsvBuilder.Amount(row.CreditTotal),
                CsvBuilder.Amount(row.NetBalance));
        }

        var fileName = $"trial-balance-{CsvBuilder.RangeSuffix(request.FromDate, request.ToDate)}.csv";
        return new CsvExportResult(csv.ToUtf8Bytes(), fileName);
    }
}
