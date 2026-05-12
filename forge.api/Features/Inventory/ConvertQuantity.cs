using FluentValidation;
using MediatR;

using Forge.Core.Interfaces;

namespace Forge.Api.Features.Inventory;

public record ConvertQuantityQuery(int FromUomId, int ToUomId, decimal Quantity, int? PartId = null)
    : IRequest<ConvertQuantityResult>;

public record ConvertQuantityResult(decimal ConvertedQuantity, decimal ConversionFactor);

public class ConvertQuantityValidator : AbstractValidator<ConvertQuantityQuery>
{
    public ConvertQuantityValidator()
    {
        RuleFor(x => x.FromUomId).GreaterThan(0);
        RuleFor(x => x.ToUomId).GreaterThan(0);
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public class ConvertQuantityHandler(IUomService uomService)
    : IRequestHandler<ConvertQuantityQuery, ConvertQuantityResult>
{
    public async Task<ConvertQuantityResult> Handle(ConvertQuantityQuery request, CancellationToken ct)
    {
        var result = await uomService.ConvertAsync(
            request.Quantity, request.FromUomId, request.ToUomId, request.PartId, ct);

        var factor = request.Quantity != 0 ? result / request.Quantity : 0;

        return new ConvertQuantityResult(result, factor);
    }
}
