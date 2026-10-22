using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Reports;

public record CreateReportScheduleCommand(
    int SavedReportId,
    string CronExpression,
    string RecipientEmailsJson,
    ReportExportFormat Format,
    string? SubjectTemplate,
    int? RequestingUserId = null) : IRequest<ReportScheduleResponseModel>;

public class CreateReportScheduleCommandValidator : AbstractValidator<CreateReportScheduleCommand>
{
    public CreateReportScheduleCommandValidator()
    {
        RuleFor(x => x.SavedReportId)
            .GreaterThan(0).WithMessage("SavedReportId is required.");

        RuleFor(x => x.CronExpression)
            .NotEmpty().WithMessage("Cron expression is required.")
            .MaximumLength(100).WithMessage("Cron expression must not exceed 100 characters.")
            .Must(ReportScheduleCron.IsValid).WithMessage("Cron expression is not valid.").WithErrorCode("cron-invalid");

        RuleFor(x => x.RecipientEmailsJson)
            .NotEmpty().WithMessage("At least one recipient email is required.")
            .Must(ReportScheduleRecipients.IsValid).WithMessage("Recipients must be a JSON array of e-mail addresses.");

        RuleFor(x => x.SubjectTemplate)
            .MaximumLength(500).When(x => x.SubjectTemplate is not null);
    }
}

public class CreateReportScheduleHandler(
    AppDbContext db,
    ReportScheduleSender sender,
    IClock clock) : IRequestHandler<CreateReportScheduleCommand, ReportScheduleResponseModel>
{
    public async Task<ReportScheduleResponseModel> Handle(CreateReportScheduleCommand request, CancellationToken cancellationToken)
    {
        var report = await db.SavedReports.FindAsync([request.SavedReportId], cancellationToken)
            ?? throw new KeyNotFoundException($"Saved report {request.SavedReportId} not found.");

        if (request.RequestingUserId is { } userId && report.UserId != userId && !report.IsShared)
            throw new KeyNotFoundException($"Saved report {request.SavedReportId} not found.");

        var schedule = new ReportSchedule
        {
            SavedReportId = request.SavedReportId,
            CronExpression = request.CronExpression,
            RecipientEmailsJson = request.RecipientEmailsJson,
            Format = request.Format,
            SubjectTemplate = request.SubjectTemplate,
            IsActive = true,
            NextRunAt = await sender.NextRunAfterAsync(request.CronExpression, clock.UtcNow, cancellationToken),
        };

        db.ReportSchedules.Add(schedule);
        await db.SaveChangesAsync(cancellationToken);

        return new ReportScheduleResponseModel(
            schedule.Id,
            schedule.SavedReportId,
            report.Name,
            schedule.CronExpression,
            schedule.RecipientEmailsJson,
            schedule.Format,
            schedule.IsActive,
            schedule.LastSentAt,
            schedule.NextRunAt,
            schedule.SubjectTemplate,
            schedule.CreatedAt,
            schedule.UpdatedAt);
    }
}
