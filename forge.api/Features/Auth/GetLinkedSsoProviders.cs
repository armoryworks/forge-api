using MediatR;
using Microsoft.AspNetCore.Identity;

using Forge.Data.Context;

namespace Forge.Api.Features.Auth;

public record LinkedSsoProviderInfo(string Provider, bool Linked);

public record GetLinkedSsoProvidersQuery(int UserId) : IRequest<List<LinkedSsoProviderInfo>>;

public class GetLinkedSsoProvidersHandler(
    UserManager<ApplicationUser> userManager) : IRequestHandler<GetLinkedSsoProvidersQuery, List<LinkedSsoProviderInfo>>
{
    public async Task<List<LinkedSsoProviderInfo>> Handle(GetLinkedSsoProvidersQuery request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString())
            ?? throw new KeyNotFoundException("User not found");

        return
        [
            new LinkedSsoProviderInfo("google", !string.IsNullOrEmpty(user.GoogleId)),
            new LinkedSsoProviderInfo("microsoft", !string.IsNullOrEmpty(user.MicrosoftId)),
            new LinkedSsoProviderInfo("oidc", !string.IsNullOrEmpty(user.OidcSubjectId)),
        ];
    }
}
