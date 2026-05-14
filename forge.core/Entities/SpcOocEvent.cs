
namespace Forge.Core.Entities;

public class SpcOocEvent : BaseEntity
{
    public int CharacteristicId { get; set; }
    public int MeasurementId { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public SpcOocSeverity Severity { get; set; }
    public SpcOocStatus Status { get; set; } = SpcOocStatus.Open;
    public int? AcknowledgedById { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public string? AcknowledgmentNotes { get; set; }
    public int? CapaId { get; set; }

    public SpcCharacteristic Characteristic { get; set; } = null!;
    public SpcMeasurement Measurement { get; set; } = null!;
}
