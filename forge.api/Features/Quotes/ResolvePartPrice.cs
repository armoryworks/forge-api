using MediatR;

using Forge.Api.Services;

namespace Forge.Api.Features.Quotes;

/// <summary>
/// AUDIT-19-S1 (+ UI prepop, forge-ui#26): the customer-specific price-list price for a part, or null
/// when there's no applicable entry. The line dialogs call this on part-select and quantity change to
/// pre-fill the price; the quantity picks the price-list break.
/// </summary>
public record ResolvePartPriceQuery(int CustomerId, int PartId, decimal? Quantity = null) : IRequest<decimal?>;

public class ResolvePartPriceHandler(CustomerPriceResolver resolver)
    : IRequestHandler<ResolvePartPriceQuery, decimal?>
{
    public Task<decimal?> Handle(ResolvePartPriceQuery request, CancellationToken ct)
        => resolver.ResolveUnitPriceAsync(request.CustomerId, request.PartId, request.Quantity, ct);
}
