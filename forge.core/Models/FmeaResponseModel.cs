using Forge.Core.Enums;

namespace Forge.Core.Models;

public record FmeaResponseModel
{
    public int Id { get; init; }
    public string FmeaNumber { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public FmeaType Type { get; init; }
    public int? PartId { get; init; }
    public string? PartNumber { get; init; }
    public int? OperationId { get; init; }
    public string? OperationName { get; init; }
    public FmeaStatus Status { get; init; }
    public string? PreparedBy { get; init; }
    public string? Responsibility { get; init; }
    public DateOnly? OriginalDate { get; init; }
    public DateOnly? RevisionDate { get; init; }
    public int RevisionNumber { get; init; }
    public int? PpapSubmissionId { get; init; }
    public int HighRpnCount { get; init; }
    public int MaxRpn { get; init; }
    public IReadOnlyList<FmeaItemResponseModel> Items { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; }
}
