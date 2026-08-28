using MediatR;
using Microsoft.AspNetCore.Identity;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Auth;

/// <param name="SetupRequired">No admin exists yet — the first-run wizard is open.</param>
/// <param name="ActivationRequired">
/// This install was provisioned for someone: the wizard will not create the first
/// admin without an activation code, which the customer gets from their contact at
/// Forge. Always false once setup is complete, and on a self-hosted install.
/// </param>
public record SetupStatusResponseModel(bool SetupRequired, bool ActivationRequired);

public record CheckSetupStatusQuery : IRequest<SetupStatusResponseModel>;

public class CheckSetupStatusHandler(
    UserManager<ApplicationUser> userManager, ISetupActivationGuard activation)
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
        var setupRequired = admins.Count == 0;
        return new SetupStatusResponseModel(setupRequired, setupRequired && activation.IsRequired);
    }
}
