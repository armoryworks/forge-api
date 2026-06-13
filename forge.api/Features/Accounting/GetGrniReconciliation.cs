using MediatR;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Phase-2 STAGE D.3 — GRNI reconciliation + aging API surface. A thin query delegating to the
/// <see cref="IGrniReconciliationService"/> read seam (GL GRNI balance vs operational received-not-billed,
/// aged, with a line-level uncovered-receipt drill-down).
/// <para><b>Gated</b> on <c>CAP-ACCT-FULLGL</c> — the reconciliation only means anything once GRNI is being
/// posted; OFF by default, so the path is unreachable (the controller is additionally Controller-role per
/// §5.7).</para>
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record GetGrniReconciliationQuery(int BookId, DateOnly? AsOfDate = null)
    : IRequest<GrniReconciliation>;

public class GetGrniReconciliationHandler(IGrniReconciliationService grniService)
    : IRequestHandler<GetGrniReconciliationQuery, GrniReconciliation>
{
    public Task<GrniReconciliation> Handle(GetGrniReconciliationQuery request, CancellationToken cancellationToken)
        => grniService.GetGrniReconciliationAsync(request.BookId, request.AsOfDate, cancellationToken);
}
