using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Parts;

public record GetPartByIdQuery(int Id) : IRequest<PartDetailResponseModel>;

public class GetPartByIdHandler(IPartRepository repo) : IRequestHandler<GetPartByIdQuery, PartDetailResponseModel>
{
    public async Task<PartDetailResponseModel> Handle(GetPartByIdQuery request, CancellationToken cancellationToken)
    {
        var part = await repo.GetDetailAsync(request.Id, cancellationToken);
        return part ?? throw new KeyNotFoundException($"Part {request.Id} not found");
    }
}
