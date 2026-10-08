using Forge.Core.Enums;

namespace Forge.Core.Models;

public record RaiseKioskAndonRequestModel
{
    public int JobId { get; init; }
    public AndonAlertType Type { get; init; }
    public string? Notes { get; init; }
}
