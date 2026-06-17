using MediatR;

using Forge.Api.Services;

namespace Forge.Api.Features.Quotes;

/// <summary>
/// AUDIT-19-S1 (+ UI prepop, forge-ui#26): the customer-specific price-list price for a part, or null
/// when there's no applicable entry. The line dialogs call this on part-select to pre-fill the price.
/// </summary>
public record ResolvePartPriceQuery(int CustomerId, int PartId) : IRequest<decimal?>;

public class ResolvePartPriceHandler(CustomerPriceResolver resolver)
    : IRequestHandler<ResolvePartPriceQuery, decimal?>
{
    public Task<decimal?> Handle(ResolvePartPriceQuery request, CancellationToken ct)
        => resolver.ResolveUnitPriceAsync(request.CustomerId, request.PartId, ct);
}
