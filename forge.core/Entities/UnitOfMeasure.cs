
namespace Forge.Core.Entities;

public class UnitOfMeasure : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public UomCategory Category { get; set; }
    public int DecimalPlaces { get; set; } = 2;
    public bool IsBaseUnit { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public ICollection<UomConversion> ConversionsFrom { get; set; } = [];
    public ICollection<UomConversion> ConversionsTo { get; set; } = [];
}
