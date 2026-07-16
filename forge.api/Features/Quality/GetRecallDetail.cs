using MediatR;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record GetRecallDetailQuery(int Id) : IRequest<RecallDetailResponseModel>;

public class GetRecallDetailHandler(AppDbContext db) : IRequestHandler<GetRecallDetailQuery, RecallDetailResponseModel>
{
    public Task<RecallDetailResponseModel> Handle(GetRecallDetailQuery request, CancellationToken cancellationToken)
        => RecallMapping.LoadDetailAsync(db, request.Id, cancellationToken);
}
