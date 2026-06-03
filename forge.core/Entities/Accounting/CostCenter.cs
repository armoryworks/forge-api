namespace Forge.Core.Entities.Accounting;

/// <summary>
/// A reporting dimension on every <c>JournalLine</c> (alongside GL Account and
/// Job). Seeded from departments / work centers (§5.1). The existing <c>Job</c>
/// entity is reused for the Job dimension.
/// </summary>
public class CostCenter : BaseEntity
{
    public int BookId { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public int? ParentId { get; set; }

    public bool IsActive { get; set; } = true;

    public Book Book { get; set; } = null!;
    public CostCenter? Parent { get; set; }
    public ICollection<CostCenter> Children { get; set; } = [];
}
