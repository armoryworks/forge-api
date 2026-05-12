using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Training;

public record GetTrainingPathQuery(int Id, int UserId, bool IsAdmin) : IRequest<TrainingPathResponseModel>;

public class GetTrainingPathHandler(AppDbContext db)
    : IRequestHandler<GetTrainingPathQuery, TrainingPathResponseModel>
{
    public async Task<TrainingPathResponseModel> Handle(GetTrainingPathQuery request, CancellationToken ct)
    {
        var query = db.TrainingPaths
            .AsNoTracking()
            .Include(p => p.PathModules)
                .ThenInclude(pm => pm.Module)
            .Where(p => p.Id == request.Id);

        if (!request.IsAdmin)
            query = query.Where(p => p.IsActive);

        var path = await query.FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Training path {request.Id} not found.");

        var moduleIds = path.PathModules.Select(pm => pm.ModuleId).ToList();
        var progressMap = await db.TrainingProgress
            .AsNoTracking()
            .Where(p => p.UserId == request.UserId && moduleIds.Contains(p.ModuleId))
            .ToDictionaryAsync(p => p.ModuleId, ct);

        return GetTrainingPathsHandler.MapPath(path, progressMap);
    }
}
