using MediatR;
using Microsoft.AspNetCore.Identity;
using Forge.Data.Context;

namespace Forge.Api.Features.Auth;

public record SetupStatusResponseModel(bool SetupRequired);

public record CheckSetupStatusQuery : IRequest<SetupStatusResponseModel>;

public class CheckSetupStatusHandler(UserManager<ApplicationUser> userManager)
    : IRequestHandler<CheckSetupStatusQuery, SetupStatusResponseModel>
{
    public async Task<SetupStatusResponseModel> Handle(CheckSetupStatusQuery request, CancellationToken cancellationToken)
    {
        // Setup is "required" until at least one Admin user exists. Counting
        // ALL users (the pre-fix behaviour) tripped to "setup complete" the
        // moment the LeadIntake first-boot bootstrap created its headless
        // service user — leaving fresh installs unable to reach the wizard
        // because the only user in the system couldn't log in interactively
        // (password disabled). Role-gated check is the right contract:
        // setup is complete iff a human admin can sign in.
        var admins = await userManager.GetUsersInRoleAsync("Admin");
        return new SetupStatusResponseModel(SetupRequired: admins.Count == 0);
    }
}
