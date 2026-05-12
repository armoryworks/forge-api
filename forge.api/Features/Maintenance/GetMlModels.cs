using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Maintenance;

public record GetMlModelsQuery : IRequest<List<MlModelResponseModel>>;

public class GetMlModelsHandler(AppDbContext db)
    : IRequestHandler<GetMlModelsQuery, List<MlModelResponseModel>>
{
    public async Task<List<MlModelResponseModel>> Handle(
        GetMlModelsQuery request, CancellationToken cancellationToken)
    {
        return await db.MlModels
            .AsNoTracking()
            .Include(m => m.WorkCenter)
            .OrderByDescending(m => m.TrainedAt)
            .Select(m => new MlModelResponseModel
            {
                ModelId = m.ModelId,
                Name = m.Name,
                ModelType = m.ModelType,
                Version = m.Version,
                Status = m.Status,
                TrainedAt = m.TrainedAt,
                TrainingSampleCount = m.TrainingSampleCount,
                Accuracy = m.Accuracy,
                Precision = m.Precision,
                Recall = m.Recall,
                F1Score = m.F1Score,
                PredictionType = m.PredictionType,
                WorkCenterName = m.WorkCenter != null ? m.WorkCenter.Name : null,
            })
            .ToListAsync(cancellationToken);
    }
}
