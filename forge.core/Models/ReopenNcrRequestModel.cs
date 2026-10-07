namespace Forge.Core.Models;

public record ReopenNcrRequestModel
{
    public string Reason { get; init; } = string.Empty;
}
