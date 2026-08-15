namespace Forge.Core.Models;

/// <summary>One table loaded by an import. <see cref="DroppedColumns"/> lists dumped columns the
/// current schema no longer has (the load uses the intersection).</summary>
public record ImportedTableResultModel(
    string Qualified,
    long Rows,
    List<string> DroppedColumns);
