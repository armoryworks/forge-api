namespace Forge.Platform.Enums;

/// <summary>
/// Phase 1r / Batch 13 — "can we actually make this?" gate distinct
/// from sales-quality outcomes. Manufacturers see leads they're
/// physically not equipped to fulfill (wrong process, wrong material,
/// wrong size envelope, wrong tolerance class). Coding those as a
/// separate axis from sales losses (lost-to-competitor / pricing /
/// timing) gives marketing a real signal about which lead sources
/// are misaligned to the shop's capabilities.
/// </summary>
public enum CapabilityFitStatus
{
    /// <summary>Not yet assessed — default for new leads.</summary>
    NotAssessed,

    /// <summary>Confirmed capable — the shop can quote and deliver.</summary>
    Fits,

    /// <summary>Won't fit our process / equipment / certs (NOT a sales loss).</summary>
    DoesntFit,

    /// <summary>Needs an engineer to confirm before quoting (envelope check / material compat).</summary>
    NeedsReview,
}

/// <summary>
/// Phase 1r / Batch 14 — NDA / confidentiality lifecycle. Manufacturers
/// almost always sign an NDA before exchanging detailed drawings or
/// process specifics. Gating the technical-detail UI behind this state
/// makes "did we get the NDA back?" answerable from the lead detail
/// instead of digging through email.
/// </summary>
public enum NdaState
{
    /// <summary>No NDA needed (or not yet discussed).</summary>
    None,
    /// <summary>Prospect requested an NDA — sales sent the template.</summary>
    Requested,
    /// <summary>NDA executed by both parties; technical detail can flow.</summary>
    InForce,
    /// <summary>NDA was signed but the term expired.</summary>
    Expired,
}

/// <summary>
/// Phase 1r / Batch 14 — export-control clearance state for aerospace/
/// defense work. ITAR / EAR-controlled tech requires citizenship +
/// end-use checks before any technical detail leaves the building.
/// </summary>
public enum ExportControlClearance
{
    /// <summary>Not applicable — domestic / non-regulated work.</summary>
    NotApplicable,
    /// <summary>Required but pending — compliance hasn't cleared the prospect yet.</summary>
    Pending,
    /// <summary>Cleared — technical detail can flow under the appropriate license.</summary>
    Cleared,
    /// <summary>Denied — we can't engage with this prospect on the requested scope.</summary>
    Denied,
}
