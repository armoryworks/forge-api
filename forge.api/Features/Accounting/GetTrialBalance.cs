using MediatR;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Phase-0 trial-balance API surface (§5.5 / §5.9 acceptance: "produce a trial
/// balance"). Mirrors the codebase's MediatR query/handler feature pattern
/// (cf. <c>Features/PurchaseOrders/GetPurchaseOrderById</c>): a thin query that
/// delegates to the read seam <see cref="ITrialBalanceService"/>, which produces
/// a filter-immune trial balance asserting total Dr == total Cr (§5.3).
/// <para>
/// <b>DARK in Phase 0.</b> Carries <see cref="RequiresCapabilityAttribute"/> for
/// <c>CAP-ACCT-FULLGL</c>; with that capability OFF both capability gates
/// short-circuit the request, so the ledger read path is unreachable.
/// </para>
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record GetTrialBalanceQuery(
    int BookId,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null)
    : IRequest<TrialBalance>;

public class GetTrialBalanceHandler(ITrialBalanceService trialBalanceService)
    : IRequestHandler<GetTrialBalanceQuery, TrialBalance>
{
    public Task<TrialBalance> Handle(GetTrialBalanceQuery request, CancellationToken cancellationToken)
        => trialBalanceService.GetTrialBalanceAsync(
            request.BookId, request.FromDate, request.ToDate, cancellationToken);
}
