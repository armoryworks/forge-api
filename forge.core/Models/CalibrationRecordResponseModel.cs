using Forge.Core.Enums;

namespace Forge.Core.Models;

public record CalibrationRecordResponseModel(
    int Id,
    int GageId,
    int CalibratedById,
    DateTimeOffset CalibratedAt,
    CalibrationResult Result,
    string? LabName,
    int? CertificateFileId,
    string? StandardsUsed,
    string? AsFoundCondition,
    string? AsLeftCondition,
    DateOnly? NextCalibrationDue,
    string? Notes);
