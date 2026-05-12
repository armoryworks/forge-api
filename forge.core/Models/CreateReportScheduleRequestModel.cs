using Forge.Core.Enums;

namespace Forge.Core.Models;

public record CreateReportScheduleRequestModel(
    int SavedReportId,
    string CronExpression,
    string RecipientEmailsJson,
    ReportExportFormat Format,
    string? SubjectTemplate);
