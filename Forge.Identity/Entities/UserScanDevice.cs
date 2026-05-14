namespace Forge.Identity.Entities;

public class UserScanDevice : BaseEntity
{
    public int UserId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public DateTimeOffset PairedAt { get; set; }
    public bool IsActive { get; set; } = true;
}
