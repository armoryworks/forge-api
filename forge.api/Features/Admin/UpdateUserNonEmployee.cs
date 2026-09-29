using MediatR;
using Microsoft.AspNetCore.Identity;

using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Admin;

public record UpdateUserNonEmployeeCommand(int UserId, bool IsNonEmployee) : IRequest;

public record UpdateUserNonEmployeeRequestModel(bool IsNonEmployee);

public class UpdateUserNonEmployeeHandler(
    UserManager<ApplicationUser> userManager,
    AppDbContext db)
    : IRequestHandler<UpdateUserNonEmployeeCommand>
{
    public async Task Handle(UpdateUserNonEmployeeCommand request, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString())
            ?? throw new KeyNotFoundException($"User {request.UserId} not found");

        if (user.IsNonEmployee == request.IsNonEmployee) return;

        user.IsNonEmployee = request.IsNonEmployee;
        await userManager.UpdateAsync(user);

        var description = request.IsNonEmployee
            ? $"Marked {user.Email} as not an employee — hiring paperwork (W-4, I-9, withholding) no longer collected or chased"
            : $"Marked {user.Email} as an employee again — hiring paperwork applies, and anything previously collected is still on file";

        db.LogActivityAt("non-employee-changed", description, ("ApplicationUser", user.Id));
        await db.SaveChangesAsync(ct);
    }
}
