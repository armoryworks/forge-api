using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.UserPreferences;

public record GetUserPreferencesQuery(int UserId) : IRequest<List<UserPreferenceResponseModel>>;

public class GetUserPreferencesHandler(IUserPreferenceRepository repo)
    : IRequestHandler<GetUserPreferencesQuery, List<UserPreferenceResponseModel>>
{
    public Task<List<UserPreferenceResponseModel>> Handle(
        GetUserPreferencesQuery request, CancellationToken cancellationToken)
        => repo.GetByUserIdAsync(request.UserId, cancellationToken);
}
