namespace Forge.Core.Models;

/// <summary>⚡ BANKING BOUNDARY — outcome of applying one bank ACH return/NOC file (Phase C).</summary>
public record BankReturnsImportResultModel(
    int Entries,
    int PaymentsReturned,
    int PrenotesRejected,
    int Nocs,
    int Unmatched,
    int AlreadyApplied);
