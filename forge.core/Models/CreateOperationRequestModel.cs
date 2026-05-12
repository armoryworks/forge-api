namespace Forge.Core.Models;

public record CreateOperationRequestModel(
    int StepNumber,
    string Title,
    string? Instructions,
    int? WorkCenterId,
    int? EstimatedMinutes,
    bool IsQcCheckpoint,
    string? QcCriteria,
    int? ReferencedOperationId,
    // Phase 3 H5 / WU-13 — subcontract metadata. When IsSubcontract is true
    // these two fields are required (validated in CreateOperation handler).
    bool? IsSubcontract = null,
    int? SubcontractVendorId = null,
    decimal? SubcontractTurnTimeDays = null);
