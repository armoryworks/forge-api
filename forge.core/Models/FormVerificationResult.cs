namespace Forge.Core.Models;

/// <summary>
/// Result of verifying an extracted ComplianceFormDefinition against the raw PDF data.
/// </summary>
public record FormVerificationResult(
    bool Passed,
    double FieldCoveragePercent,
    double TextCoveragePercent,
    List<string> MissingFieldIds,
    List<string> OrphanedFieldIds,
    List<string> Issues,
    string? CorrectedJson,
    bool VisualVerificationPassed = false,
    List<string>? VisualIssues = null);
