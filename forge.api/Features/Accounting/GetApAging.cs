using MediatR;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Phase-2 STAGE A — AP sub-ledger aging API surface (the AP counterpart of
/// <see cref="GetArAgingQuery"/>). A thin query delegating to the <see cref="IApAgingService"/> read
/// seam, which derives the aging from posted AP-control <c>JournalLine</c>s carrying a Vendor party.
/// <para><b>Gated</b> with <see cref="RequiresCapabilityAttribute"/> for <c>CAP-ACCT-FULLGL</c>; OFF by
/// default, so the ledger read path is unreachable (the controller is additionally
/// <c>[Authorize(Roles = "Controller")]</c> per §5.7).</para>
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record GetApAgingQuery(int BookId, DateOnly? AsOfDate = null)
    : IRequest<ApAging>;

public class GetApAgingHandler(IApAgingService apAgingService)
    : IRequestHandler<GetApAgingQuery, ApAging>
{
    public Task<ApAging> Handle(GetApAgingQuery request, CancellationToken cancellationToken)
        => apAgingService.GetApAgingAsync(request.BookId, request.AsOfDate, cancellationToken);
}
