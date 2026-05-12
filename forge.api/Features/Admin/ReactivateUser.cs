using MediatR;

using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record ReactivateUserCommand(int UserId) : IRequest;

public class ReactivateUserHandler(AppDbContext db)
    : IRequestHandler<ReactivateUserCommand>
{
    public async Task Handle(ReactivateUserCommand request, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([request.UserId], ct)
            ?? throw new KeyNotFoundException($"User {request.UserId} not found");

        user.IsActive = true;
        await db.SaveChangesAsync(ct);
    }
}
