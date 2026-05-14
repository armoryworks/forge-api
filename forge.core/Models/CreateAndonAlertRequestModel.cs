
namespace Forge.Core.Models;

public record CreateAndonAlertRequestModel
{
    public int WorkCenterId { get; init; }
    public AndonAlertType Type { get; init; }
    public string? Notes { get; init; }
    public int? JobId { get; init; }
}
