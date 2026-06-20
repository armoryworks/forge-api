using System.ComponentModel.DataAnnotations;

namespace Forge.Core.Entities;

/// <summary>
/// A logical, versioned generated/attached document (e.g. a wrapped shipping label). Distinct from the
/// QMS <see cref="ControlledDocument"/> system — this is a lightweight, multi-entity, auto-versioned
/// file store. The current version is the <see cref="DocumentSetVersion"/> with a null EffectiveTo;
/// links attach the set to one or more entities (Shipment, SalesOrder, Job, Invoice, …).
/// </summary>
public class DocumentSet : BaseAuditableEntity
{
    /// <summary>The document kind, e.g. "ship-label". One general store, many kinds.</summary>
    [MaxLength(50)]
    public string Kind { get; set; } = string.Empty;

    public int? CreatedBy { get; set; }

    public ICollection<DocumentSetVersion> Versions { get; set; } = [];
    public ICollection<DocumentSetLink> Links { get; set; } = [];
}
