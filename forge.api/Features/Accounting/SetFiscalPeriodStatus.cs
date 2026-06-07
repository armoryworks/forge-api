using MediatR;

using Forge.Api.Capabilities;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Phase-3 — period close/reopen. Transitions a fiscal period's status (soft-close, hard-close, reopen).
/// <para><b>Gated</b> on <c>CAP-ACCT-FULLGL</c> (a ledger operation; OFF by default, so unreachable) and the
/// controller is additionally Controller-role per §5.7.</para>
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record SetFiscalPeriodStatusCommand(int PeriodId, FiscalPeriodStatus Target)
    : IRequest<FiscalPeriodModel>;

public class SetFiscalPeriodStatusHandler(IFiscalPeriodCloseService closeService)
    : IRequestHandler<SetFiscalPeriodStatusCommand, FiscalPeriodModel>
{
    public Task<FiscalPeriodModel> Handle(SetFiscalPeriodStatusCommand request, CancellationToken cancellationToken)
        => closeService.TransitionAsync(request.PeriodId, request.Target, cancellationToken);
}
