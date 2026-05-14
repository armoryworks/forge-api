
namespace Forge.Core.Models;

public record ReportScheduleResponseModel(
    int Id,
    int SavedReportId,
    string ReportName,
    string CronExpression,
    string RecipientEmailsJson,
    ReportExportFormat Format,
    bool IsActive,
    DateTimeOffset? LastSentAt,
    DateTimeOffset? NextRunAt,
    string? SubjectTemplate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
