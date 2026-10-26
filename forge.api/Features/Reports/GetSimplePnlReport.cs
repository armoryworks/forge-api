using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Reports;

public record GetSimplePnlReportQuery(
    DateTimeOffset Start,
    DateTimeOffset End) : IRequest<List<SimplePnlReportItem>>;

public class GetSimplePnlReportHandler(
    IReportRepository repo,
    AppDbContext? db = null,
    IFinancialStatementService? statements = null,
    ICapabilitySnapshotProvider? capabilities = null)
    : IRequestHandler<GetSimplePnlReportQuery, List<SimplePnlReportItem>>
{
    private const string FullGlCapability = "CAP-ACCT-FULLGL";

    public async Task<List<SimplePnlReportItem>> Handle(GetSimplePnlReportQuery request, CancellationToken cancellationToken)
    {
        var bookId = db is not null && statements is not null && capabilities?.IsEnabled(FullGlCapability) == true
            ? await db.Books.AsNoTracking()
                .Where(b => b.IsActive)
                .OrderBy(b => b.Id)
                .Select(b => (int?)b.Id)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        if (bookId is not int id)
            return await repo.GetSimplePnlAsync(request.Start, request.End, cancellationToken);

        var pnl = await statements!.GetProfitAndLossAsync(
            id,
            DateOnly.FromDateTime(request.Start.UtcDateTime),
            DateOnly.FromDateTime(request.End.UtcDateTime),
            ct: cancellationToken);

        return pnl.Income
            .Select(l => new SimplePnlReportItem(Label(l), "Revenue", l.Amount, SimplePnlReportItem.LedgerBasis))
            .Concat(pnl.Expense
                .OrderByDescending(l => l.Amount)
                .Select(l => new SimplePnlReportItem(Label(l), "Expense", l.Amount, SimplePnlReportItem.LedgerBasis)))
            .ToList();
    }

    private static string Label(ProfitAndLossLine line) =>
        $"{line.AccountNumber} {line.AccountName}";
}
