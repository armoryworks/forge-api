namespace Forge.Core.Entities;

/// <summary>
/// One opaque state blob per external world/session, keyed by a caller-supplied
/// string id. forge-api does not interpret <see cref="Blob"/> — it is whatever
/// JSON the caller PUTs, stored and returned verbatim. Built for the factory game
/// adapter's checkpoint persistence (see factory/docs/inventory.md B70): the
/// adapter has no Postgres client of its own (D10), so it needs somewhere to
/// PUT/GET a serialized sim World and this is that somewhere — a narrow,
/// dedicated surface rather than a repurposed SystemSetting (descriptor-gated,
/// 8000-char cap) or UserPreference (bulk-only GET, wrong semantics).
///
/// No ActivityLog on mutation, deliberately: this is infrastructure cache data,
/// not a business entity, and the whole reason D6/D10 exist is that the
/// ActivityLog-per-mutation tax is disqualifying at tick-adjacent write rates.
/// </summary>
public class Checkpoint : BaseAuditableEntity
{
    public string WorldId { get; set; } = string.Empty;
    public string Blob { get; set; } = string.Empty;
}
