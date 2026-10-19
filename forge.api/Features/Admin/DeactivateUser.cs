using System.Text.Json;

using MediatR;

using Forge.Api.Services;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record DeactivateUserCommand(int UserId) : IRequest;

public class DeactivateUserHandler(
    AppDbContext db,
    UserDeactivation deactivation,
    ISystemAuditWriter auditWriter)
    : IRequestHandler<DeactivateUserCommand>
{
    public async Task Handle(DeactivateUserCommand request, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([request.UserId], ct)
            ?? throw new KeyNotFoundException($"User {request.UserId} not found");

        await UserDeactivation.EnsureNotLastActiveAdminAsync(db, user.Id, ct);

        user.IsActive = false;
        await db.SaveChangesAsync(ct);

        var result = await deactivation.ApplyAsync(user.Id, ct);

        await auditWriter.WriteAsync("UserDeactivated", db.CurrentUserId ?? 0,
            entityType: "ApplicationUser",
            entityId: user.Id,
            details: JsonSerializer.Serialize(result),
            ct: ct);
    }
}
