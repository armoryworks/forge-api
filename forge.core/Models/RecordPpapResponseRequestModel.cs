using Forge.Core.Enums;

namespace Forge.Core.Models;

public record RecordPpapResponseRequestModel
{
    public PpapStatus CustomerDecision { get; init; }
    public string? Notes { get; init; }
}
