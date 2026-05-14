
namespace Forge.Core.Models;

public record DispositionNcrRequestModel
{
    public NcrDispositionCode Code { get; init; }
    public string? Notes { get; init; }
    public string? ReworkInstructions { get; init; }
}
