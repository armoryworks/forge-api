using MediatR;

using Forge.Core.Models;

namespace Forge.Api.Features.Shipping;

public record PrintPickListQuery(int WaveId) : IRequest<PickWaveResponseModel>;

public class PrintPickListHandler(IMediator mediator) : IRequestHandler<PrintPickListQuery, PickWaveResponseModel>
{
    public Task<PickWaveResponseModel> Handle(PrintPickListQuery request, CancellationToken cancellationToken) =>
        mediator.Send(new GetPickWaveQuery(request.WaveId), cancellationToken);
}
