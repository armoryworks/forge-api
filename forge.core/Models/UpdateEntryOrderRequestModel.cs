namespace Forge.Core.Models;

public record UpdateEntryOrderRequestModel(List<EntryOrderItem> Items);
public record EntryOrderItem(int JobId, int SortOrder);
