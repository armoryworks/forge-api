namespace Forge.Core.Enums;

/// <summary>C2: per-row outcome of a customer bulk-intake preview/commit.</summary>
public enum BulkCustomerIntakeRowStatus
{
    Created,
    Invalid,
    DuplicateWithinBatch,
    DuplicateExistingCustomer,
}
