namespace Forge.Core.Models;

/// <summary>
/// Wire shape for one capability-requirement row. Returned by the admin
/// list/get endpoints (<c>EntityCapabilityRequirementsController</c>) and
/// referenced from the per-entity completeness response. No relations are
/// included — predicate evaluation happens server-side and the chip only
/// needs the labels, so consumers don't need the raw predicate JSON.
/// </summary>
public record EntityCapabilityRequirementResponseModel(
    int Id,
    string EntityType,
    string CapabilityCode,
    string RequirementId,
    string Predicate,
    string DisplayNameKey,
    string MissingMessageKey,
    int SortOrder,
    bool IsSeedData);
