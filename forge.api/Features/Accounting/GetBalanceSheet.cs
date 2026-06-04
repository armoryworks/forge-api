using MediatR;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Phase-1 STAGE E — Balance Sheet API surface (ACCOUNTING_SUITE_PLAN §6 Phase-1
/// row "P&amp;L + Balance Sheet"). Mirrors the codebase's MediatR query/handler
/// feature pattern (cf. <see cref="GetTrialBalanceQuery"/>): a thin query that
/// delegates to the read seam <see cref="IFinancialStatementService"/>, which
/// projects Asset/Liability/Equity accounts as of a date from posted
/// <c>JournalLine</c>s (filter-immune) plus a computed current-year-earnings
/// equity line.
/// <para>
/// <b>Dual-gated</b> exactly like <see cref="GetProfitAndLossQuery"/>:
/// <c>CAP-ACCT-FULLGL</c> here (GL engine gate) + <c>CAP-RPT-FINANCIALS</c> on the
/// controller endpoint (financial-statements reporting gate). Both default OFF.
/// </para>
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record GetBalanceSheetQuery(int BookId, DateOnly? AsOfDate = null)
    : IRequest<BalanceSheet>;

public class GetBalanceSheetHandler(IFinancialStatementService financialStatementService)
    : IRequestHandler<GetBalanceSheetQuery, BalanceSheet>
{
    public Task<BalanceSheet> Handle(GetBalanceSheetQuery request, CancellationToken cancellationToken)
        => financialStatementService.GetBalanceSheetAsync(
            request.BookId, request.AsOfDate, cancellationToken);
}
