namespace Forge.Core.Models;

/// <summary>One whitelisted field of <c>GET /api/v1/jobs</c>: the key clients sort and filter by.</summary>
/// <param name="Key">The sort and filter key; matches the response property name.</param>
/// <param name="Kind">How the field filters.</param>
/// <param name="Gate">The role-gated group the field belongs to.</param>
/// <param name="Sortable">Whether <c>sort=</c> accepts the key.</param>
/// <param name="Filterable">Whether <c>filter=</c> accepts the key.</param>
public record JobListField(string Key, JobListFieldKind Kind, JobListFieldGate Gate, bool Sortable = true, bool Filterable = true);
