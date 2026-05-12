namespace Forge.Core.Entities;

public class SerialHistory : BaseEntity
{
    public int SerialNumberId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? FromLocationName { get; set; }
    public string? ToLocationName { get; set; }
    public int? ActorId { get; set; }
    public string? Details { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    public SerialNumber SerialNumber { get; set; } = null!;
}
