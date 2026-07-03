using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>
/// regulated-parts-safety C-1: the additive union of the shop's active compliance profiles —
/// the strictest required traceability, whether SDS is required, and the merged required-field
/// rules. "general/none" contributes nothing.
/// </summary>
public record EffectiveComplianceRequirements(
    TraceabilityType RequiredTraceabilityType,
    bool SdsRequired,
    IReadOnlyList<RequiredComplianceField> RequiredFields);
