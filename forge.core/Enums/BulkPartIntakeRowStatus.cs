namespace Forge.Core.Enums;

/// <summary>Per-row outcome of a part bulk-intake preview/commit.</summary>
public enum BulkPartIntakeRowStatus
{
    Created,
    Invalid,
    DuplicateWithinBatch,
    DuplicateExistingPart,
}
