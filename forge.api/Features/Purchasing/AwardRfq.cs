using MediatR;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Purchasing;

public record AwardRfqCommand(int RfqId, int VendorResponseId) : IRequest<int>;

public class AwardRfqHandler(IRfqService rfqService)
    : IRequestHandler<AwardRfqCommand, int>
{
    public async Task<int> Handle(AwardRfqCommand request, CancellationToken cancellationToken)
    {
        return await rfqService.AwardAndCreatePoAsync(request.RfqId, request.VendorResponseId, cancellationToken);
    }
}
