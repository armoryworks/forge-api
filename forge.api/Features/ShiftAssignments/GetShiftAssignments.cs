using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ShiftAssignments;

public record GetShiftAssignmentsQuery(int? UserId = null) : IRequest<List<ShiftAssignmentResponseModel>>;

public class GetShiftAssignmentsHandler(AppDbContext db) : IRequestHandler<GetShiftAssignmentsQuery, List<ShiftAssignmentResponseModel>>
{
    public async Task<List<ShiftAssignmentResponseModel>> Handle(GetShiftAssignmentsQuery request, CancellationToken cancellationToken)
    {
        var query = db.ShiftAssignments.AsNoTracking()
            .Include(sa => sa.Shift)
            .AsQueryable();

        if (request.UserId.HasValue)
            query = query.Where(sa => sa.UserId == request.UserId.Value);

        return await query
            .OrderByDescending(sa => sa.EffectiveFrom)
            .Join(db.Users, sa => sa.UserId, u => u.Id, (sa, u) => new ShiftAssignmentResponseModel(
                sa.Id, sa.UserId,
                u.LastName + ", " + u.FirstName,
                sa.ShiftId, sa.Shift.Name,
                sa.EffectiveFrom, sa.EffectiveTo,
                sa.ShiftDifferentialRate, sa.Notes))
            .ToListAsync(cancellationToken);
    }
}
