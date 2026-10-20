using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Employees;

public record CanViewEmployeeQuery(int UserId, int CallerUserId, bool CallerIsAdmin) : IRequest<bool>;

public class CanViewEmployeeHandler(AppDbContext db) : IRequestHandler<CanViewEmployeeQuery, bool>
{
    public async Task<bool> Handle(CanViewEmployeeQuery request, CancellationToken ct)
    {
        var teamId = await EmployeeTeamScope.RestrictedTeamIdAsync(db, request.CallerUserId, request.CallerIsAdmin, ct);
        if (teamId is null)
            return true;

        return await db.Users.AnyAsync(u => u.Id == request.UserId && u.TeamId == teamId, ct);
    }
}
