using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Forge.Api.Features.Admin;

public record RoleItem(string Name);

public record GetRolesQuery : IRequest<List<RoleItem>>;

public class GetRolesHandler(RoleManager<IdentityRole<int>> roleManager)
    : IRequestHandler<GetRolesQuery, List<RoleItem>>
{
    public async Task<List<RoleItem>> Handle(GetRolesQuery request, CancellationToken cancellationToken)
    {
        var roles = await roleManager.Roles
            .OrderBy(r => r.Name)
            .Select(r => new RoleItem(r.Name!))
            .ToListAsync(cancellationToken);

        return roles;
    }
}
