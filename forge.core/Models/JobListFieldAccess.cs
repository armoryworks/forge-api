namespace Forge.Core.Models;

/// <summary>
/// Which role-gated groups of job-list fields the caller may read. A field in a group the caller lacks is
/// returned as null, cannot be sorted on, and cannot be filtered on, so the jobs table and board reveal
/// exactly what the caller could read from the API that owns that data.
/// </summary>
/// <param name="Customer">Customer name and id, and customer-name text search.</param>
/// <param name="SalesOrder">The originating sales order's id and number.</param>
/// <param name="Billing">Billing status and the accounting document reference.</param>
/// <param name="Cost">Estimated cost and quoted price.</param>
public record JobListFieldAccess(bool Customer, bool SalesOrder, bool Billing, bool Cost)
{
    /// <summary>Every field group; for system callers and tests.</summary>
    public static JobListFieldAccess Full { get; } = new(true, true, true, true);

    /// <summary>True when the caller may read fields guarded by <paramref name="gate"/>.</summary>
    public bool Allows(JobListFieldGate gate) => gate switch
    {
        JobListFieldGate.Customer => Customer,
        JobListFieldGate.SalesOrder => SalesOrder,
        JobListFieldGate.Billing => Billing,
        JobListFieldGate.Cost => Cost,
        _ => true,
    };
}
