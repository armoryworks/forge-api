namespace Forge.Core.Models;

/// <summary>
/// Result of validating or committing a CSV import. A commit is all-or-nothing: when any row is invalid
/// nothing is written and <see cref="Committed"/> is false.
/// </summary>
public record DataImportResultModel(
    int TotalRows,
    int ValidCount,
    int InvalidCount,
    int CreatedCount,
    bool Committed,
    List<DataImportRowResultModel> Rows);
