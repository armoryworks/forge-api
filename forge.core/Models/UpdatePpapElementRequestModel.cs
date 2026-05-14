
namespace Forge.Core.Models;

public record UpdatePpapElementRequestModel
{
    public PpapElementStatus? Status { get; init; }
    public string? Notes { get; init; }
    public int? AssignedToUserId { get; init; }
}
