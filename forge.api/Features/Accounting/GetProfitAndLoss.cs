using MediatR;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Phase-1 STAGE E — Profit &amp; Loss API surface (ACCOUNTING_SUITE_PLAN §6
/// Phase-1 row "P&amp;L + Balance Sheet"). Mirrors the codebase's MediatR
/// query/handler feature pattern (cf. <see cref="GetTrialBalanceQuery"/>): a thin
/// query that delegates to the read seam <see cref="IFinancialStatementService"/>,
/// which projects Income/Expense accounts over the period range from posted
/// <c>JournalLine</c>s (filter-immune).
/// <para>
/// <b>Dual-gated.</b> The MediatR <c>CapabilityGateBehavior</c> reads
/// <see cref="RequiresCapabilityAttribute"/> for <c>CAP-ACCT-FULLGL</c> (the GL
/// engine gate — default OFF, keeps the read path dark). The controller endpoint
/// additionally carries <c>CAP-RPT-FINANCIALS</c> (the financial-statements
/// reporting gate, default OFF until COGS posting is live — §6 / §10). The
/// attribute only allows one capability per type, so the FULLGL gate lives here
/// and the FINANCIALS gate lives on the endpoint; both must be ON to reach the
/// handler.
/// </para>
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record GetProfitAndLossQuery(
    int BookId,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null)
    : IRequest<ProfitAndLoss>;

public class GetProfitAndLossHandler(IFinancialStatementService financialStatementService)
    : IRequestHandler<GetProfitAndLossQuery, ProfitAndLoss>
{
    public Task<ProfitAndLoss> Handle(GetProfitAndLossQuery request, CancellationToken cancellationToken)
        => financialStatementService.GetProfitAndLossAsync(
            request.BookId, request.FromDate, request.ToDate, cancellationToken);
}
