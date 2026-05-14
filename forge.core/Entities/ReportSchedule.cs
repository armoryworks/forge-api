
namespace Forge.Core.Entities;

public class ReportSchedule : BaseAuditableEntity
{
    public int SavedReportId { get; set; }
    public string CronExpression { get; set; } = string.Empty;
    public string RecipientEmailsJson { get; set; } = "[]";
    public ReportExportFormat Format { get; set; } = ReportExportFormat.Pdf;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastSentAt { get; set; }
    public DateTimeOffset? NextRunAt { get; set; }
    public string? SubjectTemplate { get; set; }

    public SavedReport SavedReport { get; set; } = null!;
}
