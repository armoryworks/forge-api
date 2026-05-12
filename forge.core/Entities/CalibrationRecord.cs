using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class CalibrationRecord : BaseEntity
{
    public int GageId { get; set; }
    public int CalibratedById { get; set; }
    public DateTimeOffset CalibratedAt { get; set; }
    public CalibrationResult Result { get; set; }
    public string? LabName { get; set; }
    public int? CertificateFileId { get; set; }
    public string? StandardsUsed { get; set; }
    public string? AsFoundCondition { get; set; }
    public string? AsLeftCondition { get; set; }
    public DateOnly? NextCalibrationDue { get; set; }
    public string? Notes { get; set; }

    public Gage Gage { get; set; } = null!;
    public FileAttachment? CertificateFile { get; set; }
}
