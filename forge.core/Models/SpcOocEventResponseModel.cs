namespace Forge.Core.Models;

public record SpcOocEventResponseModel
{
    public int Id { get; init; }
    public int CharacteristicId { get; init; }
    public string CharacteristicName { get; init; } = string.Empty;
    public string PartNumber { get; init; } = string.Empty;
    public int MeasurementId { get; init; }
    public DateTimeOffset DetectedAt { get; init; }
    public string RuleName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? AcknowledgedByName { get; init; }
    public DateTimeOffset? AcknowledgedAt { get; init; }
    public string? AcknowledgmentNotes { get; init; }
    public int? CapaId { get; init; }
}
