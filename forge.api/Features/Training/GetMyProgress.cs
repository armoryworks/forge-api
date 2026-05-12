using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Training;

public record GetMyProgressQuery(int UserId) : IRequest<List<TrainingProgressResponseModel>>;

public class GetMyProgressHandler(AppDbContext db)
    : IRequestHandler<GetMyProgressQuery, List<TrainingProgressResponseModel>>
{
    public async Task<List<TrainingProgressResponseModel>> Handle(
        GetMyProgressQuery request, CancellationToken ct)
    {
        var progress = await db.TrainingProgress
            .AsNoTracking()
            .Where(p => p.UserId == request.UserId)
            .ToListAsync(ct);

        return progress.Select(p => new TrainingProgressResponseModel(
            p.ModuleId,
            p.Status,
            p.QuizScore,
            p.QuizAttempts,
            p.StartedAt,
            p.CompletedAt,
            p.TimeSpentSeconds
        )).ToList();
    }
}
