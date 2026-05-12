namespace Forge.Core.Entities;

public class Notification : BaseAuditableEntity
{
    public int UserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string Source { get; set; } = "system";
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public bool IsPinned { get; set; }
    public bool IsDismissed { get; set; }
    public string? EntityType { get; set; }
    public int? EntityId { get; set; }
    public int? SenderId { get; set; }
}
