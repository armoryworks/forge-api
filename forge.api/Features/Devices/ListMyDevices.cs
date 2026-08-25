using System.Security.Claims;

using MediatR;

namespace Forge.Api.Features.Devices;

public record ListMyDevicesQuery : IRequest<List<DeviceResponseModel>>;

public class ListMyDevicesHandler(
    IMediator mediator,
    IHttpContextAccessor httpContext)
    : IRequestHandler<ListMyDevicesQuery, List<DeviceResponseModel>>
{
    public async Task<List<DeviceResponseModel>> Handle(
        ListMyDevicesQuery request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(
            httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return await mediator.Send(new ListDevicesQuery(userId), cancellationToken);
    }
}
