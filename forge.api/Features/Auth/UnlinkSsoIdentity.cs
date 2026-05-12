using FluentValidation;

using MediatR;
using Microsoft.AspNetCore.Identity;

using Forge.Data.Context;

namespace Forge.Api.Features.Auth;

public record UnlinkSsoIdentityCommand(int UserId, string Provider) : IRequest;

public class UnlinkSsoIdentityValidator : AbstractValidator<UnlinkSsoIdentityCommand>
{
    public UnlinkSsoIdentityValidator()
    {
        RuleFor(x => x.Provider).NotEmpty().Must(p => p is "google" or "microsoft" or "oidc")
            .WithMessage("Provider must be 'google', 'microsoft', or 'oidc'.");
    }
}

public class UnlinkSsoIdentityHandler(
    UserManager<ApplicationUser> userManager) : IRequestHandler<UnlinkSsoIdentityCommand>
{
    public async Task Handle(UnlinkSsoIdentityCommand request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString())
            ?? throw new KeyNotFoundException("User not found");

        switch (request.Provider)
        {
            case "google": user.GoogleId = null; break;
            case "microsoft": user.MicrosoftId = null; break;
            case "oidc":
                user.OidcSubjectId = null;
                user.OidcProvider = null;
                break;
        }

        await userManager.UpdateAsync(user);
    }
}
