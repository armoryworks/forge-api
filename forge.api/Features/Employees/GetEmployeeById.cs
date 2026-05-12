using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Employees;

/// <summary>
/// Phase 3 / WU-19 / F9 — fetch an Employee by EmployeeProfile.Id (works for
/// both linked and User-less employees). Distinct from
/// <c>GetEmployeeDetailQuery</c> which keys on User.Id for backward compat.
/// </summary>
public record GetEmployeeByIdQuery(int EmployeeId) : IRequest<EmployeeProfileResponseModel?>;

public class GetEmployeeByIdHandler(AppDbContext db)
    : IRequestHandler<GetEmployeeByIdQuery, EmployeeProfileResponseModel?>
{
    public async Task<EmployeeProfileResponseModel?> Handle(GetEmployeeByIdQuery request, CancellationToken ct)
    {
        var profile = await db.EmployeeProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.EmployeeId, ct);

        if (profile is null) return null;
        return CreateEmployeeHandler.Project(profile);
    }
}
