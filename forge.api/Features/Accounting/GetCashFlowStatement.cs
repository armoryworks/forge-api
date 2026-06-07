using MediatR;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Phase-3 — indirect-method Cash-Flow statement. Dual-gated like the P&amp;L / Balance Sheet:
/// <c>CAP-RPT-FINANCIALS</c> at the HTTP edge + <c>CAP-ACCT-FULLGL</c> on the controller.
/// </summary>
[RequiresCapability("CAP-RPT-FINANCIALS")]
public record GetCashFlowStatementQuery(int BookId, DateOnly? FromDate = null, DateOnly? ToDate = null)
    : IRequest<CashFlowStatement>;

public class GetCashFlowStatementHandler(ICashFlowStatementService service)
    : IRequestHandler<GetCashFlowStatementQuery, CashFlowStatement>
{
    public Task<CashFlowStatement> Handle(GetCashFlowStatementQuery request, CancellationToken cancellationToken)
        => service.GetCashFlowStatementAsync(request.BookId, request.FromDate, request.ToDate, cancellationToken);
}
