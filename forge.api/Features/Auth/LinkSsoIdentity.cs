using FluentValidation;

using MediatR;
using Microsoft.AspNetCore.Identity;

using Forge.Data.Context;

namespace Forge.Api.Features.Auth;

public record LinkSsoIdentityCommand(int UserId, string Provider, string ExternalId) : IRequest;

public class LinkSsoIdentityValidator : AbstractValidator<LinkSsoIdentityCommand>
{
    public LinkSsoIdentityValidator()
    {
        RuleFor(x => x.Provider).NotEmpty().Must(p => p is "google" or "microsoft" or "oidc")
            .WithMessage("Provider must be 'google', 'microsoft', or 'oidc'.");
        RuleFor(x => x.ExternalId).NotEmpty();
    }
}

public class LinkSsoIdentityHandler(
    UserManager<ApplicationUser> userManager) : IRequestHandler<LinkSsoIdentityCommand>
{
    public async Task Handle(LinkSsoIdentityCommand request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString())
            ?? throw new KeyNotFoundException("User not found");

        switch (request.Provider)
        {
            case "google": user.GoogleId = request.ExternalId; break;
            case "microsoft": user.MicrosoftId = request.ExternalId; break;
            case "oidc":
                user.OidcSubjectId = request.ExternalId;
                user.OidcProvider = "oidc";
                break;
        }

        await userManager.UpdateAsync(user);
    }
}
