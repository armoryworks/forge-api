using MediatR;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Phase-1 STAGE D AR sub-ledger + aging API surface (ACCOUNTING_SUITE_PLAN §6
/// Phase-1 row "AR sub-ledger + aging"). Mirrors the codebase's MediatR
/// query/handler feature pattern (cf. <see cref="GetTrialBalanceQuery"/>): a thin
/// query that delegates to the read seam <see cref="IArAgingService"/>, which
/// derives the aging from posted AR-control <c>JournalLine</c>s carrying a
/// Customer party and bucketed by age (filter-immune, with an
/// AR-control-vs-aging reconciliation).
/// <para>
/// <b>Gated.</b> Carries <see cref="RequiresCapabilityAttribute"/> for
/// <c>CAP-ACCT-FULLGL</c>; with that capability OFF (the default) both capability
/// gates short-circuit the request, so the ledger read path is unreachable
/// (the controller is additionally <c>[Authorize(Roles = "Controller")]</c> per
/// §5.7).
/// </para>
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record GetArAgingQuery(int BookId, DateOnly? AsOfDate = null)
    : IRequest<ArAging>;

public class GetArAgingHandler(IArAgingService arAgingService)
    : IRequestHandler<GetArAgingQuery, ArAging>
{
    public Task<ArAging> Handle(GetArAgingQuery request, CancellationToken cancellationToken)
        => arAgingService.GetArAgingAsync(request.BookId, request.AsOfDate, cancellationToken);
}
