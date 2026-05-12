namespace Forge.Core.Models;

public record UpdateCustomerRequestModel(
    string? Name,
    string? CompanyName,
    string? Email,
    string? Phone,
    bool? IsActive,
    // Phase 1r / Batch 15-16 — regulated-industry flags + reference-customer
    // consent. Each is independently nullable so a chip-toggle on the
    // customer detail panel only PATCHes the field it changed.
    bool? IsFdaRegulated = null,
    bool? IsAerospace = null,
    bool? IsAutomotive = null,
    bool? IsItarControlled = null,
    bool? IsReferenceOk = null,
    string? ReferenceNotes = null);
