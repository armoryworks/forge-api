using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Assets;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Maintenance;

public record SchedulePreventiveMaintenanceCommand(int Id) : IRequest;

public class SchedulePreventiveMaintenanceHandler(
    AppDbContext db,
    IJobRepository jobRepo,
    IBusinessIdentifierService identifiers,
    IPredictiveMaintenanceService predMaintService)
    : IRequestHandler<SchedulePreventiveMaintenanceCommand>
{
    public async Task Handle(SchedulePreventiveMaintenanceCommand command, CancellationToken cancellationToken)
    {
        var prediction = await db.MaintenancePredictions
            .Include(p => p.WorkCenter)
            .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Prediction {command.Id} not found");

        if (prediction.Status == MaintenancePredictionStatus.MaintenanceScheduled || prediction.PreventiveMaintenanceJobId.HasValue)
            throw new InvalidOperationException("Maintenance already scheduled for this prediction");

        if (prediction.Status is MaintenancePredictionStatus.Resolved
            or MaintenancePredictionStatus.FalsePositive
            or MaintenancePredictionStatus.Expired)
            throw new InvalidOperationException($"A {prediction.Status} prediction cannot be scheduled for maintenance");

        await predMaintService.ScheduleMaintenanceAsync(command.Id, cancellationToken);

        var workCenterName = prediction.WorkCenter?.Name ?? $"Work center {prediction.WorkCenterId}";
        var job = await MaintenanceJobFactory.AddAsync(
            db,
            jobRepo,
            $"Maintenance: {workCenterName} — {prediction.PredictionType}",
            $"Preventive maintenance for predicted {prediction.PredictionType} on {workCenterName} ({prediction.ConfidencePercent:0.#}% confidence).",
            prediction.PredictedFailureDate,
            cancellationToken);

        prediction.PreventiveMaintenanceJob = job;
        prediction.Status = MaintenancePredictionStatus.MaintenanceScheduled;
        await db.SaveChangesAsync(cancellationToken);
        await identifiers.IssueAsync(BusinessEntityType.Job, job.Id, job.JobNumber, cancellationToken);
    }
}
