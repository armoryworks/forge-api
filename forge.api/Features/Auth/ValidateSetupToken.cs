using MediatR;
using Microsoft.AspNetCore.Identity;

using Forge.Data.Context;

namespace Forge.Api.Features.Auth;

public record ValidateSetupTokenQuery(string Token) : IRequest<SetupTokenInfoResponse>;

public record SetupTokenInfoResponse(string FirstName, string LastName, string Email);

public class ValidateSetupTokenHandler(
    UserManager<ApplicationUser> userManager) : IRequestHandler<ValidateSetupTokenQuery, SetupTokenInfoResponse>
{
    public Task<SetupTokenInfoResponse> Handle(ValidateSetupTokenQuery request, CancellationToken cancellationToken)
    {
        var tokenHash = Admin.CreateAdminUserHandler.HashSetupToken(request.Token);
        var user = userManager.Users
            .Where(u => u.SetupToken == tokenHash && u.SetupTokenExpiresAt > DateTimeOffset.UtcNow)
            .FirstOrDefault()
            ?? throw new KeyNotFoundException("Invalid or expired setup token");

        return Task.FromResult(new SetupTokenInfoResponse(user.FirstName, user.LastName, user.Email!));
    }
}
