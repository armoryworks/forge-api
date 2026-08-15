namespace Forge.Core.Models;

/// <summary>Child rows whose parent didn't make the trip — found by the post-import FK
/// re-validation (the load runs with FK triggers suspended).</summary>
public record FkOrphanInfoModel(
    string Constraint,
    string ChildTable,
    string ParentTable,
    long Rows);
