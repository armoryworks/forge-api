using FluentValidation;
using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Shipments;

public record GetShippingRatesQuery(int ShipmentId, GetShippingRatesRequestModel Request) : IRequest<List<ShippingRate>>;

public class GetShippingRatesValidator : AbstractValidator<GetShippingRatesQuery>
{
    public GetShippingRatesValidator()
    {
        RuleFor(x => x.ShipmentId).GreaterThan(0);
        RuleFor(x => x.Request.FromAddress).NotNull();
        RuleFor(x => x.Request.ToAddress).NotNull();
        RuleFor(x => x.Request.Packages).NotEmpty().WithMessage("At least one package is required");
    }
}

public class GetShippingRatesHandler(IShippingService shippingService)
    : IRequestHandler<GetShippingRatesQuery, List<ShippingRate>>
{
    public async Task<List<ShippingRate>> Handle(GetShippingRatesQuery request, CancellationToken cancellationToken)
    {
        var shipmentRequest = new ShipmentRequest(
            request.Request.FromAddress,
            request.Request.ToAddress,
            request.Request.Packages,
            request.Request.ServiceType);

        return await shippingService.GetRatesAsync(shipmentRequest, cancellationToken);
    }
}
