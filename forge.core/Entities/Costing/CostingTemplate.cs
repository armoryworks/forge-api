using System.ComponentModel.DataAnnotations;

namespace Forge.Core.Entities.Costing;

/// <summary>
/// A reusable costing setup package: a named list of overhead categories
/// (<see cref="CostingTemplateLine"/>) that the quick-start applies — each line
/// becomes an overhead pool with a budget, and optionally a GL expense account +
/// budget line. System templates ship with the product; users build their own.
/// </summary>
public class CostingTemplate : BaseAuditableEntity
{
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(512)]
    public string? Description { get; set; }

    /// <summary>Shipped with the product — editable, but never deletable.</summary>
    public bool IsSystem { get; set; }

    public ICollection<CostingTemplateLine> Lines { get; set; } = [];
}
