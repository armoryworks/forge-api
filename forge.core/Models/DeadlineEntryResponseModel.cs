namespace Forge.Core.Models;

public record DeadlineEntryResponseModel(string Date, string JobNumber, string Description, bool IsOverdue);
