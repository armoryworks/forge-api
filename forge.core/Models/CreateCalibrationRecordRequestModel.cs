
namespace Forge.Core.Models;

public class CreateCalibrationRecordRequestModel
{
    public DateTimeOffset CalibratedAt { get; set; }
    public CalibrationResult Result { get; set; }
    public string? LabName { get; set; }
    public int? CertificateFileId { get; set; }
    public string? StandardsUsed { get; set; }
    public string? AsFoundCondition { get; set; }
    public string? AsLeftCondition { get; set; }
    public string? Notes { get; set; }
}
