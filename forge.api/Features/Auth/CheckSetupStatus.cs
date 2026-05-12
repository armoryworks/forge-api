using MediatR;
using Microsoft.AspNetCore.Identity;
using Forge.Data.Context;

namespace Forge.Api.Features.Auth;

public record SetupStatusResponseModel(bool SetupRequired);

public record CheckSetupStatusQuery : IRequest<SetupStatusResponseModel>;

public class CheckSetupStatusHandler(UserManager<ApplicationUser> userManager)
    : IRequestHandler<CheckSetupStatusQuery, SetupStatusResponseModel>
{
    public Task<SetupStatusResponseModel> Handle(CheckSetupStatusQuery request, CancellationToken cancellationToken)
    {
        var anyUsers = userManager.Users.Any();
        return Task.FromResult(new SetupStatusResponseModel(!anyUsers));
    }
}
