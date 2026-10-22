namespace Forge.Core.Models;

public record JobOperationTimerStopResponseModel(
    bool Stopped,
    TimeEntryResponseModel? Entry,
    JobOperationRowResponseModel? Operation = null);
