namespace Forge.Core.Models;

public record UpdateOperationRequestModel(
    int? StepNumber,
    string? Title,
    string? Instructions,
    int? WorkCenterId,
    int? EstimatedMinutes,
    bool? IsQcCheckpoint,
    string? QcCriteria,
    int? ReferencedOperationId,
    // Phase 3 H5 / WU-13 — subcontract metadata. When the resulting state
    // has IsSubcontract = true, both vendor + turn-time must be present
    // (validated in UpdateOperation handler).
    bool? IsSubcontract = null,
    int? SubcontractVendorId = null,
    decimal? SubcontractTurnTimeDays = null);
