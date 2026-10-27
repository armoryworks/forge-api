using MediatR;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Parts;

public record GetMaterialSpecsQuery(bool IncludeRetired) : IRequest<List<MaterialSpecResponseModel>>;

public class GetMaterialSpecsHandler(AppDbContext db) : IRequestHandler<GetMaterialSpecsQuery, List<MaterialSpecResponseModel>>
{
    public Task<List<MaterialSpecResponseModel>> Handle(GetMaterialSpecsQuery request, CancellationToken cancellationToken)
        => MaterialSpecLookup.ListAsync(db, request.IncludeRetired, cancellationToken);
}
