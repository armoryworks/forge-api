using MediatR;

namespace Forge.Api.Features.Mrp;

public record BulkReleasePlannedOrdersCommand(List<int> Ids) : IRequest<List<ReleasePlannedOrderResult>>;

public class BulkReleasePlannedOrdersHandler(IMediator mediator)
    : IRequestHandler<BulkReleasePlannedOrdersCommand, List<ReleasePlannedOrderResult>>
{
    public async Task<List<ReleasePlannedOrderResult>> Handle(BulkReleasePlannedOrdersCommand request, CancellationToken cancellationToken)
    {
        var results = new List<ReleasePlannedOrderResult>();

        foreach (var id in request.Ids)
        {
            var result = await mediator.Send(new ReleasePlannedOrderCommand(id), cancellationToken);
            results.Add(result);
        }

        return results;
    }
}
