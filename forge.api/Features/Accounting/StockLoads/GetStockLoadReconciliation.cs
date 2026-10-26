using MediatR;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;

namespace Forge.Api.Features.Accounting.StockLoads;

/// <summary>
/// The inventory GL vs valuation-store tie-out, reachable at cutover (before the GL view is on) so the opening
/// journal and opening stock screens can show that the ledger and the stock agree.
/// </summary>
[RequiresAnyCapability("CAP-ACCT-MIGRATION", "CAP-ACCT-FULLGL")]
public record GetStockLoadReconciliationQuery(int BookId) : IRequest<InventoryValuationReconciliation>;

public class GetStockLoadReconciliationHandler(IInventoryValuationService valuation)
    : IRequestHandler<GetStockLoadReconciliationQuery, InventoryValuationReconciliation>
{
    public Task<InventoryValuationReconciliation> Handle(GetStockLoadReconciliationQuery request, CancellationToken ct)
        => valuation.ReconcileAsync(request.BookId, ct);
}
