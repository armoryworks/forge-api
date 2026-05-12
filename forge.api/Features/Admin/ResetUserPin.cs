using MediatR;
using Microsoft.AspNetCore.Identity;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record ResetUserPinCommand(int UserId) : IRequest;

public class ResetUserPinHandler(
    UserManager<ApplicationUser> userManager) : IRequestHandler<ResetUserPinCommand>
{
    public async Task Handle(ResetUserPinCommand request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString())
            ?? throw new KeyNotFoundException($"User {request.UserId} not found");

        user.PinHash = null;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await userManager.UpdateAsync(user);
    }
}
