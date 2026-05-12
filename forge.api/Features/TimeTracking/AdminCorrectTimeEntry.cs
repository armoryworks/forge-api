using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.TimeTracking;

public record AdminCorrectTimeEntryCommand(int Id, int CorrectedByUserId, AdminCorrectTimeEntryRequestModel Data)
    : IRequest<TimeEntryResponseModel>;

public class AdminCorrectTimeEntryValidator : AbstractValidator<AdminCorrectTimeEntryCommand>
{
    public AdminCorrectTimeEntryValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Data.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Data.DurationMinutes).InclusiveBetween(1, 1440).When(x => x.Data.DurationMinutes.HasValue);
        RuleFor(x => x.Data.StartTime).LessThan(x => x.Data.EndTime)
            .When(x => x.Data.StartTime.HasValue && x.Data.EndTime.HasValue)
            .WithMessage("Start time must be before end time.");
        RuleFor(x => x.Data.Category).MaximumLength(100).When(x => x.Data.Category is not null);
        RuleFor(x => x.Data.Notes).MaximumLength(1000).When(x => x.Data.Notes is not null);
    }
}

public class AdminCorrectTimeEntryHandler(
    AppDbContext db,
    ITimeTrackingRepository repo,
    IMediator mediator) : IRequestHandler<AdminCorrectTimeEntryCommand, TimeEntryResponseModel>
{
    public async Task<TimeEntryResponseModel> Handle(AdminCorrectTimeEntryCommand request, CancellationToken cancellationToken)
    {
        var entry = await repo.FindTimeEntryAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Time entry {request.Id} not found.");

        // Snapshot original values before correction
        var correction = new TimeCorrectionLog
        {
            TimeEntryId = entry.Id,
            CorrectedByUserId = request.CorrectedByUserId,
            Reason = request.Data.Reason.Trim(),
            OriginalJobId = entry.JobId,
            OriginalDate = entry.Date,
            OriginalDurationMinutes = entry.DurationMinutes,
            OriginalStartTime = entry.TimerStart,
            OriginalEndTime = entry.TimerStop,
            OriginalCategory = entry.Category,
            OriginalNotes = entry.Notes,
        };
        db.TimeCorrectionLogs.Add(correction);

        // Apply corrections (no date/lock restrictions for admin)
        var data = request.Data;
        if (data.JobId.HasValue) entry.JobId = data.JobId;
        if (data.Date.HasValue) entry.Date = data.Date.Value;
        if (data.StartTime.HasValue) entry.TimerStart = data.StartTime.Value;
        if (data.EndTime.HasValue) entry.TimerStop = data.EndTime.Value;
        // Auto-calculate duration from start/end if both are provided
        if (data.StartTime.HasValue && data.EndTime.HasValue)
            entry.DurationMinutes = (int)Math.Round((data.EndTime.Value - data.StartTime.Value).TotalMinutes);
        else if (data.DurationMinutes.HasValue)
            entry.DurationMinutes = data.DurationMinutes.Value;
        if (data.Category is not null) entry.Category = data.Category.Trim();
        if (data.Notes is not null) entry.Notes = data.Notes.Trim();

        await db.SaveChangesAsync(cancellationToken);

        // Notify the affected employee
        var correctorName = await db.Users
            .Where(u => u.Id == request.CorrectedByUserId)
            .Select(u => u.LastName + ", " + u.FirstName)
            .FirstOrDefaultAsync(cancellationToken) ?? "A manager";

        await mediator.Send(new Forge.Api.Features.Notifications.CreateNotificationCommand(
            new CreateNotificationRequestModel(
                UserId: entry.UserId,
                Type: "time_entry_corrected",
                Severity: "info",
                Source: "time_tracking",
                Title: "Time Entry Corrected",
                Message: $"{correctorName} corrected your time entry for {entry.Date:MM/dd/yyyy}. Reason: {correction.Reason}",
                EntityType: "time_entries",
                EntityId: entry.Id,
                SenderId: request.CorrectedByUserId)), cancellationToken);

        return (await repo.GetTimeEntryByIdAsync(entry.Id, cancellationToken))!;
    }
}
