using Forge.Core.Enums;

namespace Forge.Core.Models;

public record PpapElementResponseModel
{
    public int Id { get; init; }
    public int ElementNumber { get; init; }
    public string ElementName { get; init; } = string.Empty;
    public PpapElementStatus Status { get; init; }
    public bool IsRequired { get; init; }
    public string? Notes { get; init; }
    public string? AssignedToName { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public int AttachmentCount { get; init; }
}
