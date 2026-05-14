
namespace Forge.Core.Models;

public record CreateWbsCostEntryRequestModel
{
    public WbsCostCategory Category { get; init; }
    public decimal Amount { get; init; }
    public string? Description { get; init; }
    public string? SourceEntityType { get; init; }
    public int? SourceEntityId { get; init; }
    public DateTimeOffset? EntryDate { get; init; }
}
