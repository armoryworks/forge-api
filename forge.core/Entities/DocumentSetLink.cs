using System.ComponentModel.DataAnnotations;

namespace Forge.Core.Entities;

/// <summary>
/// Links a <see cref="DocumentSet"/> to an entity (polymorphic). One document can be referenced from
/// several entities at once — e.g. a wrapped shipping label linked to its Shipment, Sales Order, Job,
/// and Invoice. The link targets the set (stable across versions), not a single version.
/// </summary>
public class DocumentSetLink : BaseAuditableEntity
{
    public int DocumentSetId { get; set; }
    public DocumentSet DocumentSet { get; set; } = null!;

    [MaxLength(50)]
    public string EntityType { get; set; } = string.Empty;

    public int EntityId { get; set; }
}
