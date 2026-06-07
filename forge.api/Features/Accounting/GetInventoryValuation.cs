using MediatR;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;

namespace Forge.Api.Features.Accounting;

/// <summary>STAGE E — perpetual inventory valuation per part. CAP-ACCT-FULLGL gated.</summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record GetInventoryValuationQuery(int BookId) : IRequest<IReadOnlyList<InventoryValuationModel>>;

public class GetInventoryValuationHandler(IInventoryValuationService service)
    : IRequestHandler<GetInventoryValuationQuery, IReadOnlyList<InventoryValuationModel>>
{
    public Task<IReadOnlyList<InventoryValuationModel>> Handle(GetInventoryValuationQuery request, CancellationToken ct)
        => service.GetAsync(request.BookId, ct);
}

/// <summary>STAGE E — valuation-store vs GL inventory-control reconciliation. CAP-ACCT-FULLGL gated.</summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record GetInventoryValuationReconciliationQuery(int BookId) : IRequest<InventoryValuationReconciliation>;

public class GetInventoryValuationReconciliationHandler(IInventoryValuationService service)
    : IRequestHandler<GetInventoryValuationReconciliationQuery, InventoryValuationReconciliation>
{
    public Task<InventoryValuationReconciliation> Handle(GetInventoryValuationReconciliationQuery request, CancellationToken ct)
        => service.ReconcileAsync(request.BookId, ct);
}
