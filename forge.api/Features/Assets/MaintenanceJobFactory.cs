using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Assets;

public static class MaintenanceJobFactory
{
    public static async Task<Job> AddAsync(
        AppDbContext db,
        IJobRepository jobRepo,
        string title,
        string description,
        DateTimeOffset? dueDate,
        CancellationToken ct)
    {
        var maintenanceTrack = await db.TrackTypes
            .Include(t => t.Stages.OrderBy(s => s.SortOrder))
            .FirstOrDefaultAsync(t => t.Name.Contains("Maintenance"), ct)
            ?? throw new KeyNotFoundException("No Maintenance track type found. Create one in Admin → Track Types.");

        var firstStage = maintenanceTrack.Stages.Where(s => s.IsActive).OrderBy(s => s.SortOrder).FirstOrDefault()
            ?? throw new KeyNotFoundException("Maintenance track has no stages configured.");

        var jobNumber = await jobRepo.GenerateNextJobNumberAsync(ct);
        var maxPos = await jobRepo.GetMaxBoardPositionAsync(firstStage.Id, ct);

        var job = new Job
        {
            JobNumber = jobNumber,
            Title = title,
            Description = description,
            TrackTypeId = maintenanceTrack.Id,
            CurrentStageId = firstStage.Id,
            BoardPosition = maxPos + 1,
            DueDate = dueDate,
        };

        await jobRepo.AddAsync(job, ct);
        return job;
    }
}
