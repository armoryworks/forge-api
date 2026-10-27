namespace Forge.Core.Models;

public record TerminationEffectsModel(
    int UnassignedJobCount,
    bool StoppedTimer,
    bool ClockedOut,
    bool KioskCredentialsCleared,
    int ScanIdentifiersDeactivated,
    int PasskeysRemoved,
    int ApiKeysRevoked,
    int DevicesRevoked,
    int LeaveRequestsCancelled);
