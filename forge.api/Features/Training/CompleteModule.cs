using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Training;

public record CompleteModuleCommand(int UserId, int ModuleId) : IRequest;

public class CompleteModuleHandler(AppDbContext db) : IRequestHandler<CompleteModuleCommand>
{
    public async Task Handle(CompleteModuleCommand request, CancellationToken ct)
    {
        var progress = await db.TrainingProgress
            .FirstOrDefaultAsync(p => p.UserId == request.UserId && p.ModuleId == request.ModuleId, ct);

        if (progress is null)
        {
            progress = new TrainingProgress
            {
                UserId = request.UserId,
                ModuleId = request.ModuleId,
                Status = TrainingProgressStatus.Completed,
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
            };
            db.TrainingProgress.Add(progress);
        }
        else
        {
            progress.Status = TrainingProgressStatus.Completed;
            progress.CompletedAt ??= DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        await CheckAndCompleteEnrollmentsAsync(request.UserId, ct);
    }

    private async Task CheckAndCompleteEnrollmentsAsync(int userId, CancellationToken ct)
    {
        var enrollments = await db.TrainingPathEnrollments
            .Include(e => e.Path)
                .ThenInclude(p => p.PathModules)
            .Where(e => e.UserId == userId && e.CompletedAt == null)
            .ToListAsync(ct);

        if (enrollments.Count == 0) return;

        var allRequiredModuleIds = enrollments
            .SelectMany(e => e.Path.PathModules.Where(pm => pm.IsRequired).Select(pm => pm.ModuleId))
            .Distinct()
            .ToList();

        var completedModuleIds = await db.TrainingProgress
            .Where(p => p.UserId == userId
                        && allRequiredModuleIds.Contains(p.ModuleId)
                        && p.Status == TrainingProgressStatus.Completed)
            .Select(p => p.ModuleId)
            .ToListAsync(ct);

        var completedSet = new HashSet<int>(completedModuleIds);
        var now = DateTimeOffset.UtcNow;
        var anyChanged = false;

        foreach (var enrollment in enrollments)
        {
            var requiredIds = enrollment.Path.PathModules
                .Where(pm => pm.IsRequired)
                .Select(pm => pm.ModuleId)
                .ToList();

            if (requiredIds.Count > 0 && requiredIds.All(id => completedSet.Contains(id)))
            {
                enrollment.CompletedAt = now;
                anyChanged = true;
            }
        }

        if (anyChanged)
            await db.SaveChangesAsync(ct);
    }
}
