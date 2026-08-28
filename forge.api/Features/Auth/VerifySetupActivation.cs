using MediatR;
using Microsoft.AspNetCore.Identity;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Auth;

/// <param name="Valid">The code unlocks this install's first-run wizard.</param>
public record VerifySetupActivationResponseModel(bool Valid);

/// <summary>
/// Checks an activation code without spending the wizard on it, so the gate screen
/// can accept or reject before the customer fills in three steps of account and
/// company detail. Refuses once setup is complete — a claimed install must not keep
/// answering questions about its code.
/// </summary>
public record VerifySetupActivationQuery(string? Code) : IRequest<VerifySetupActivationResponseModel>;

public class VerifySetupActivationHandler(
    UserManager<ApplicationUser> userManager, ISetupActivationGuard activation)
    : IRequestHandler<VerifySetupActivationQuery, VerifySetupActivationResponseModel>
{
    public async Task<VerifySetupActivationResponseModel> Handle(
        VerifySetupActivationQuery request, CancellationToken cancellationToken)
    {
        var admins = await userManager.GetUsersInRoleAsync("Admin");
        if (admins.Count > 0)
            throw new InvalidOperationException("Setup has already been completed.");

        return new VerifySetupActivationResponseModel(activation.Verify(request.Code));
    }
}
