using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Users;

public record GetUsersQuery : IRequest<List<UserResponseModel>>;

public class GetUsersHandler(IUserRepository repo) : IRequestHandler<GetUsersQuery, List<UserResponseModel>>
{
    public Task<List<UserResponseModel>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
        => repo.GetAllActiveAsync(cancellationToken);
}
