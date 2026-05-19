namespace Forge.Identity.Entities;

public class KioskTerminal : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string DeviceToken { get; set; } = string.Empty;
    public int TeamId { get; set; }
    public int ConfiguredByUserId { get; set; }
    public bool IsActive { get; set; } = true;

    // Optional: kiosk physically at a single work center (e.g., the tablet
    // bolted to the CNC mill). When set, scans default the work-center
    // context so the operator doesn't have to pick it. Null = team-wide
    // kiosk (breakroom, shipping desk).
    public int? WorkCenterId { get; set; }

    public Team Team { get; set; } = null!;
    public WorkCenter? WorkCenter { get; set; }
}
