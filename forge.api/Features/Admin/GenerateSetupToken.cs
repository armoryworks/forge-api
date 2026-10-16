using MediatR;
using Microsoft.AspNetCore.Identity;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record GenerateSetupTokenCommand(int UserId) : IRequest<SetupTokenResponseModel>;

public record SetupTokenResponseModel(string Token, DateTimeOffset ExpiresAt);

public class GenerateSetupTokenHandler(
    UserManager<ApplicationUser> userManager) : IRequestHandler<GenerateSetupTokenCommand, SetupTokenResponseModel>
{
    public async Task<SetupTokenResponseModel> Handle(GenerateSetupTokenCommand request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString())
            ?? throw new KeyNotFoundException($"User {request.UserId} not found");

        var token = SetupTokenCode.Generate();

        user.SetupToken = CreateAdminUserHandler.HashSetupToken(token);
        user.SetupTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(7);
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await userManager.UpdateAsync(user);

        return new SetupTokenResponseModel(token, user.SetupTokenExpiresAt.Value);
    }
}
