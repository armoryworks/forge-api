using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>
/// Pre-beta cleanup: the legacy <c>PartType</c> / <c>Material</c> /
/// <c>MoldToolRef</c> fields are gone. New code starts a part by picking the
/// three axes via the workflow fork dialog, which routes to a per-combo
/// definition; this DTO only carries the bare fields needed to materialize a
/// row from the legacy direct-create endpoint (still used by a couple of
/// migration tests + admin tooling).
/// </summary>
public record CreatePartRequestModel(
    string Name,
    string? Description,
    string? Revision,
    ProcurementSource ProcurementSource,
    InventoryClass InventoryClass,
    int? MaterialSpecId);
