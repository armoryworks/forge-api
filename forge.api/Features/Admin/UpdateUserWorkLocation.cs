using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record UpdateUserWorkLocationCommand(int UserId, int? WorkLocationId) : IRequest;

public record UpdateUserWorkLocationRequestModel(int? WorkLocationId);

public class UpdateUserWorkLocationHandler(
    UserManager<ApplicationUser> userManager,
    AppDbContext db)
    : IRequestHandler<UpdateUserWorkLocationCommand>
{
    public async Task Handle(UpdateUserWorkLocationCommand request, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString())
            ?? throw new KeyNotFoundException($"User {request.UserId} not found");

        if (request.WorkLocationId.HasValue)
        {
            var location = await db.CompanyLocations
                .FirstOrDefaultAsync(l => l.Id == request.WorkLocationId.Value, ct)
                ?? throw new KeyNotFoundException($"Company location {request.WorkLocationId} not found");
        }

        user.WorkLocationId = request.WorkLocationId;
        await userManager.UpdateAsync(user);
    }
}
