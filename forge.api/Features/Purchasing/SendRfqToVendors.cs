using FluentValidation;
using MediatR;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Purchasing;

public record SendRfqToVendorsCommand(int RfqId, List<int> VendorIds) : IRequest;

public class SendRfqToVendorsValidator : AbstractValidator<SendRfqToVendorsCommand>
{
    public SendRfqToVendorsValidator()
    {
        RuleFor(x => x.RfqId).GreaterThan(0);
        RuleFor(x => x.VendorIds).NotEmpty().WithMessage("At least one vendor is required");
    }
}

public class SendRfqToVendorsHandler(IRfqService rfqService)
    : IRequestHandler<SendRfqToVendorsCommand>
{
    public async Task Handle(SendRfqToVendorsCommand request, CancellationToken cancellationToken)
    {
        await rfqService.SendToVendorsAsync(request.RfqId, request.VendorIds, cancellationToken);
    }
}
