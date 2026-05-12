using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Validation;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Assets;

public record CreateMaintenanceScheduleCommand(CreateMaintenanceScheduleRequestModel Data)
    : IRequest<MaintenanceScheduleResponseModel>;

public class CreateMaintenanceScheduleCommandValidator : AbstractValidator<CreateMaintenanceScheduleCommand>
{
    public CreateMaintenanceScheduleCommandValidator()
    {
        RuleFor(x => x.Data.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Data.IntervalDays).GreaterThan(0);
        RuleFor(x => x.Data.AssetId).GreaterThan(0);
    }
}

public class CreateMaintenanceScheduleHandler(AppDbContext db)
    : IRequestHandler<CreateMaintenanceScheduleCommand, MaintenanceScheduleResponseModel>
{
    public async Task<MaintenanceScheduleResponseModel> Handle(
        CreateMaintenanceScheduleCommand request, CancellationToken ct)
    {
        var data = request.Data;

        var asset = await db.Assets.FindAsync([data.AssetId], ct);
        // Phase 3 H2 / WU-12: asset-active check on maintenance-schedule
        // create. Retired or out-of-service assets cannot get new schedules.
        ActiveCheck.EnsureActive(asset, "Asset", "assetId", data.AssetId);

        var schedule = new MaintenanceSchedule
        {
            AssetId = data.AssetId,
            Title = data.Title.Trim(),
            Description = data.Description?.Trim(),
            IntervalDays = data.IntervalDays,
            IntervalHours = data.IntervalHours,
            NextDueAt = data.NextDueAt,
        };

        db.MaintenanceSchedules.Add(schedule);
        await db.SaveChangesAsync(ct);

        return new MaintenanceScheduleResponseModel(
            schedule.Id,
            schedule.AssetId,
            asset.Name,
            schedule.Title,
            schedule.Description,
            schedule.IntervalDays,
            schedule.IntervalHours,
            schedule.LastPerformedAt,
            schedule.NextDueAt,
            schedule.IsActive,
            schedule.NextDueAt < DateTimeOffset.UtcNow);
    }
}
