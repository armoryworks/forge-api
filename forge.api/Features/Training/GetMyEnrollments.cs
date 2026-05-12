using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Training;

public record GetMyEnrollmentsQuery(int UserId) : IRequest<List<TrainingEnrollmentResponseModel>>;

public class GetMyEnrollmentsHandler(AppDbContext db)
    : IRequestHandler<GetMyEnrollmentsQuery, List<TrainingEnrollmentResponseModel>>
{
    public async Task<List<TrainingEnrollmentResponseModel>> Handle(
        GetMyEnrollmentsQuery request, CancellationToken ct)
    {
        var enrollments = await db.TrainingPathEnrollments
            .AsNoTracking()
            .Include(e => e.Path)
                .ThenInclude(p => p.PathModules)
            .Where(e => e.UserId == request.UserId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(ct);

        var allModuleIds = enrollments
            .SelectMany(e => e.Path.PathModules.Select(pm => pm.ModuleId))
            .Distinct()
            .ToList();

        var progressMap = await db.TrainingProgress
            .AsNoTracking()
            .Where(p => p.UserId == request.UserId && allModuleIds.Contains(p.ModuleId))
            .ToDictionaryAsync(p => p.ModuleId, ct);

        return enrollments.Select(e =>
        {
            var totalModules = e.Path.PathModules.Count;
            var completedModules = e.Path.PathModules
                .Count(pm => progressMap.TryGetValue(pm.ModuleId, out var prog)
                             && prog.Status == TrainingProgressStatus.Completed);

            return new TrainingEnrollmentResponseModel(
                e.Id,
                e.PathId,
                e.Path.Title,
                e.Path.Icon,
                totalModules,
                completedModules,
                e.CreatedAt,
                e.CompletedAt
            );
        }).ToList();
    }
}
