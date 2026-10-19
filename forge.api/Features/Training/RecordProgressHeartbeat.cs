using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Training;

public record RecordProgressHeartbeatCommand(int UserId, int ModuleId, int Seconds) : IRequest;

public class RecordProgressHeartbeatHandler(AppDbContext db) : IRequestHandler<RecordProgressHeartbeatCommand>
{
    public const int MaxSecondsPerHeartbeat = 60;

    public async Task Handle(RecordProgressHeartbeatCommand request, CancellationToken ct)
    {
        if (!await db.TrainingModules.AsNoTracking().AnyAsync(m => m.Id == request.ModuleId, ct))
            throw new KeyNotFoundException($"Training module {request.ModuleId} not found.");

        var progress = await db.TrainingProgress
            .FirstOrDefaultAsync(p => p.UserId == request.UserId && p.ModuleId == request.ModuleId, ct);

        var seconds = Math.Clamp(request.Seconds, 0, MaxSecondsPerHeartbeat);

        if (progress is null)
        {
            db.TrainingProgress.Add(new TrainingProgress
            {
                UserId = request.UserId,
                ModuleId = request.ModuleId,
                Status = TrainingProgressStatus.InProgress,
                StartedAt = DateTimeOffset.UtcNow,
                TimeSpentSeconds = seconds,
            });
        }
        else if (progress.Status != TrainingProgressStatus.Completed)
        {
            progress.TimeSpentSeconds += seconds;
        }

        await db.SaveChangesAsync(ct);
    }
}
