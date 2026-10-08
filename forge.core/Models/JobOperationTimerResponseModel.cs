namespace Forge.Core.Models;

public record JobOperationTimerResponseModel(
    TimeEntryResponseModel Entry,
    bool AlreadyRunning,
    JobOperationRowResponseModel Operation);
