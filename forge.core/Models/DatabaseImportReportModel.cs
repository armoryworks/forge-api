namespace Forge.Core.Models;

/// <summary>
/// Outcome of a clean-rebuild import. <see cref="Success"/> is false only when FK orphans were
/// found and not explicitly allowed — the data IS loaded either way (the load transaction had
/// already committed before validation); the flag tells the operator the rebuild needs another
/// pass (re-include a parent table, or re-import with different options).
/// </summary>
public record DatabaseImportReportModel(
    bool Success,
    List<ImportedTableResultModel> Loaded,
    List<string> Excluded,
    List<string> MissingInTarget,
    long SoftDeletedPurged,
    List<FkOrphanInfoModel> FkOrphans);
