namespace Forge.Core.Enums;

/// <summary>
/// Lifecycle of a lot-based recall (CAP-QC-RECALL). A recall is an immutable snapshot
/// once initiated; only its status/resolution changes.
/// </summary>
public enum RecallStatus
{
    Active = 0,
    Resolved = 1,
    Withdrawn = 2,
}
