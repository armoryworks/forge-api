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

        var job = await MaintenanceJobFactory.AddAsync(
            db,
            jobRepo,
            $"Maintenance: {schedule.Asset.Name} — {schedule.Title}",
            schedule.Description ?? $"Scheduled maintenance for {schedule.Asset.Name}.",
            schedule.NextDueAt,
            ct);

        schedule.MaintenanceJob = job;
        await db.SaveChangesAsync(ct);

        return job.Id;
    }
}
