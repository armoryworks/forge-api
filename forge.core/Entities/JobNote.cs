namespace Forge.Core.Entities;

public class JobNote : BaseAuditableEntity
{
    public int JobId { get; set; }
    public Job Job { get; set; } = null!;
    public string Text { get; set; } = string.Empty;
    public int? CreatedBy { get; set; }
}
