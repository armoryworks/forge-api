namespace Forge.Core.Models;

/// <summary>
/// Wire shape for create / update of one capability-requirement row.
/// `Id` is null on POST; populated on PUT for update target. Validation
/// (predicate JSON parses, capability code exists in the catalog,
/// EntityType is one of the supported types) is enforced by the handler.
/// </summary>
public record UpsertEntityCapabilityRequirementRequestModel(
    string EntityType,
    string CapabilityCode,
    string RequirementId,
    string Predicate,
    string DisplayNameKey,
    string MissingMessageKey,
    int SortOrder);
