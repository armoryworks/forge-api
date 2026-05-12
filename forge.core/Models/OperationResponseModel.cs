namespace Forge.Core.Models;

public record OperationResponseModel(
    int Id,
    int PartId,
    int StepNumber,
    string Title,
    string? Instructions,
    int? WorkCenterId,
    string? WorkCenterName,
    int? EstimatedMinutes,
    bool IsQcCheckpoint,
    string? QcCriteria,
    int? ReferencedOperationId,
    string? ReferencedOperationTitle,
    List<OperationMaterialResponseModel> Materials,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    // Phase 3 H5 / WU-13 — subcontract metadata round-tripped on GET so the
    // routing-op editor and subcontract-send-out flows see vendor + turn
    // time on operations marked as subcontract.
    bool IsSubcontract = false,
    int? SubcontractVendorId = null,
    string? SubcontractVendorName = null,
    decimal? SubcontractTurnTimeDays = null);
