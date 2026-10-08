using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.ShopFloor;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Andon;

public record RaiseKioskAndonCommand(int JobId, AndonAlertType Type, string? Notes, int UserId)
    : IRequest<RaiseKioskAndonResponseModel>;

public class RaiseKioskAndonValidator : AbstractValidator<RaiseKioskAndonCommand>
{
    public static readonly IReadOnlySet<AndonAlertType> KioskTypes =
        new HashSet<AndonAlertType> { AndonAlertType.Stoppage, AndonAlertType.Quality, AndonAlertType.Material };

    public RaiseKioskAndonValidator()
    {
        RuleFor(x => x.JobId).GreaterThan(0);
        RuleFor(x => x.Type).Must(KioskTypes.Contains).WithMessage("Choose stoppage, quality or material.");
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class RaiseKioskAndonHandler(
    AppDbContext db,
    IMediator mediator,
    IJobOperationService operations,
    IClock clock) : IRequestHandler<RaiseKioskAndonCommand, RaiseKioskAndonResponseModel>
{
    public async Task<RaiseKioskAndonResponseModel> Handle(RaiseKioskAndonCommand request, CancellationToken ct)
    {
        var job = await db.Jobs
            .AsNoTracking()
            .Where(j => j.Id == request.JobId)
            .Select(j => new { j.Id, j.JobNumber })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Job {request.JobId} not found");

        var tracking = await operations.IsTrackingEnabledAsync(ct);
        var next = await KioskWork.NextOperationsAsync(db, [job.Id], tracking, ct);
        if (!next.TryGetValue(job.Id, out var operation) || operation.WorkCenterId is not int workCenterId)
            throw new InvalidOperationException(
                $"{job.JobNumber} has no work center on its current operation, so the alert has nowhere to go. Tell your supervisor.");

        var workCenterName = operation.WorkCenterName ?? string.Empty;
        db.JobActivityLogs.Add(new JobActivityLog
        {
            JobId = job.Id,
            UserId = request.UserId,
            Action = ActivityAction.AndonRaised,
            FieldName = "Andon",
            NewValue = request.Type.ToString(),
            Description = $"Raised a {request.Type.ToString().ToLowerInvariant()} andon at {workCenterName} on operation {operation.StepNumber} {operation.Title}.",
            CreatedAt = clock.UtcNow,
            OperationId = operation.OperationId,
            WorkCenterId = workCenterId,
        });

        var alert = await mediator.Send(new CreateAndonAlertCommand(new CreateAndonAlertRequestModel
        {
            WorkCenterId = workCenterId,
            Type = request.Type,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            JobId = job.Id,
        }), ct);

        return new RaiseKioskAndonResponseModel
        {
            AlertId = alert.Id,
            Type = alert.Type,
            JobId = job.Id,
            JobNumber = job.JobNumber,
            OperationId = operation.OperationId,
            OperationStepNumber = operation.StepNumber,
            OperationTitle = operation.Title,
            WorkCenterId = workCenterId,
            WorkCenterName = workCenterName,
        };
    }
}
