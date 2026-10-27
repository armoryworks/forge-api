using MediatR;

namespace Forge.Api.Features.Inventory.PhysicalInventories;

public record GetPhysicalInventoryCountSheetsQuery(int Id, bool? Blind, string? Language) : IRequest<(byte[] Content, string FileName)>;

public class GetPhysicalInventoryCountSheetsHandler(IPhysicalInventoryService service) : IRequestHandler<GetPhysicalInventoryCountSheetsQuery, (byte[] Content, string FileName)>
{
    public Task<(byte[] Content, string FileName)> Handle(GetPhysicalInventoryCountSheetsQuery request, CancellationToken cancellationToken)
        => service.BuildCountSheetsAsync(request.Id, request.Blind, request.Language, cancellationToken);
}
