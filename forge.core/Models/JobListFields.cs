namespace Forge.Core.Models;

/// <summary>
/// The whitelist of job-list fields that <c>GET /api/v1/jobs</c> sorts and filters by. Anything not listed
/// here is refused as a filter and ignored as a sort, so a client can never reach an arbitrary column.
/// Enum filters on <c>currentStageId</c>, <c>trackTypeId</c> and <c>assigneeId</c> take ids;
/// <c>priorityName</c>, <c>disposition</c> and <c>billingStatus</c> take their enum names.
/// </summary>
public static class JobListFields
{
    public static IReadOnlyList<JobListField> All { get; } =
    [
        new("id", JobListFieldKind.Number, JobListFieldGate.None),
        new("jobNumber", JobListFieldKind.Text, JobListFieldGate.None),
        new("title", JobListFieldKind.Text, JobListFieldGate.None),
        new("currentStageId", JobListFieldKind.Enum, JobListFieldGate.None),
        new("trackTypeId", JobListFieldKind.Enum, JobListFieldGate.None),
        new("assigneeId", JobListFieldKind.Enum, JobListFieldGate.None),
        new("priorityName", JobListFieldKind.Enum, JobListFieldGate.None),
        new("dueDate", JobListFieldKind.Date, JobListFieldGate.None),
        new("startDate", JobListFieldKind.Date, JobListFieldGate.None),
        new("completedDate", JobListFieldKind.Date, JobListFieldGate.None),
        new("createdAt", JobListFieldKind.Date, JobListFieldGate.None),
        new("updatedAt", JobListFieldKind.Date, JobListFieldGate.None),
        new("isOverdue", JobListFieldKind.Boolean, JobListFieldGate.None),
        new("onHold", JobListFieldKind.Boolean, JobListFieldGate.None),
        new("partNumber", JobListFieldKind.Text, JobListFieldGate.None),
        new("quantity", JobListFieldKind.Number, JobListFieldGate.None),
        new("targetQuantity", JobListFieldKind.Number, JobListFieldGate.None),
        new("parentJobNumber", JobListFieldKind.Text, JobListFieldGate.None),
        new("childJobCount", JobListFieldKind.Number, JobListFieldGate.None),
        new("disposition", JobListFieldKind.Enum, JobListFieldGate.None),
        new("customerName", JobListFieldKind.Text, JobListFieldGate.Customer),
        new("salesOrderNumber", JobListFieldKind.Text, JobListFieldGate.SalesOrder),
        new("billingStatus", JobListFieldKind.Enum, JobListFieldGate.Billing),
        new("externalRef", JobListFieldKind.Text, JobListFieldGate.Billing),
        new("estimatedTotalCost", JobListFieldKind.Number, JobListFieldGate.Cost),
        new("quotedPrice", JobListFieldKind.Number, JobListFieldGate.Cost),
    ];

    private static readonly Dictionary<string, JobListField> ByKey =
        All.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>The field for <paramref name="key"/> (any case), or null when it is not whitelisted.</summary>
    public static JobListField? Find(string? key) =>
        key is not null && ByKey.TryGetValue(key.Trim(), out var field) ? field : null;
}
