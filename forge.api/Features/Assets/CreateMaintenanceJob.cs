using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Assets;

public record CreateMaintenanceJobCommand(int MaintenanceScheduleId) : IRequest<int>;

public class CreateMaintenanceJobValidator : AbstractValidator<CreateMaintenanceJobCommand>
{
    public CreateMaintenanceJobValidator()
    {
        RuleFor(x => x.MaintenanceScheduleId).GreaterThan(0);
    }
}

public class CreateMaintenanceJobHandler(AppDbContext db, IJobRepository jobRepo) : IRequestHandler<CreateMaintenanceJobCommand, int>
{
    public async Task<int> Handle(CreateMaintenanceJobCommand request, CancellationToken ct)
    {
        var schedule = await db.Set<MaintenanceSchedule>()
            .Include(s => s.Asset)
            .FirstOrDefaultAsync(s => s.Id == request.MaintenanceScheduleId, ct)
            ?? throw new KeyNotFoundException($"MaintenanceSchedule {request.MaintenanceScheduleId} not found.");

        // Find the Maintenance track type
        var maintenanceTrack = await db.TrackTypes
            .Include(t => t.Stages.OrderBy(s => s.SortOrder))
            .FirstOrDefaultAsync(t => t.Name.Contains("Maintenance"), ct)
            ?? throw new KeyNotFoundException("No Maintenance track type found. Create one in Admin → Track Types.");

        var firstStage = maintenanceTrack.Stages.FirstOrDefault()
            ?? throw new KeyNotFoundException("Maintenance track has no stages configured.");

        var jobNumber = await jobRepo.GenerateNextJobNumberAsync(ct);
        var maxPos = await jobRepo.GetMaxBoardPositionAsync(firstStage.Id, ct);

        var job = new Job
        {
            JobNumber = jobNumber,
            Title = $"Maintenance: {schedule.Asset.Name} — {schedule.Title}",
            Description = schedule.Description ?? $"Scheduled maintenance for {schedule.Asset.Name}.",
            TrackTypeId = maintenanceTrack.Id,
            CurrentStageId = firstStage.Id,
            BoardPosition = maxPos + 1,
            DueDate = schedule.NextDueAt,
        };

        await jobRepo.AddAsync(job, ct);

        // Link schedule to job
        schedule.MaintenanceJobId = job.Id;
        await db.SaveChangesAsync(ct);

        return job.Id;
    }
}
