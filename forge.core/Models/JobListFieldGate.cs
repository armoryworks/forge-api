namespace Forge.Core.Models;

/// <summary>The role-gated group a job-list field belongs to. See <see cref="JobListFieldAccess"/>.</summary>
public enum JobListFieldGate
{
    None,
    Customer,
    SalesOrder,
    Billing,
    Cost,
}
