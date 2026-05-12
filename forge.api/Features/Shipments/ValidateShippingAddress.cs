using FluentValidation;
using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Shipments;

public record ValidateShippingAddressCommand(ValidateAddressRequestModel Request) : IRequest<AddressValidationResponseModel>;

public class ValidateShippingAddressValidator : AbstractValidator<ValidateShippingAddressCommand>
{
    public ValidateShippingAddressValidator()
    {
        RuleFor(x => x.Request.Street).NotEmpty();
        RuleFor(x => x.Request.City).NotEmpty();
        RuleFor(x => x.Request.State).NotEmpty();
        RuleFor(x => x.Request.Zip).NotEmpty();
        RuleFor(x => x.Request.Country).NotEmpty();
    }
}

public class ValidateShippingAddressHandler(IAddressValidationService addressValidationService)
    : IRequestHandler<ValidateShippingAddressCommand, AddressValidationResponseModel>
{
    public async Task<AddressValidationResponseModel> Handle(ValidateShippingAddressCommand request, CancellationToken cancellationToken)
    {
        return await addressValidationService.ValidateAsync(request.Request, cancellationToken);
    }
}
