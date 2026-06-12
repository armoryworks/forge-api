namespace Forge.Core.Models.Accounting;

/// <summary>⚡ BANK-001 — outcome of one statement file import (insert + dedupe + auto-match).</summary>
public record ImportBankStatementResultModel(
    int ImportId,
    int Imported,
    int Duplicates,
    int Suggested);
