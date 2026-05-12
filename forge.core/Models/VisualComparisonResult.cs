namespace Forge.Core.Models;

/// <summary>
/// Top-level result combining structural (pixel-level) and AI semantic visual comparison.
/// Stored as JSON on FormDefinitionVersion.
/// </summary>
public record VisualComparisonResult(
    /// <summary>
    /// Whether the structural (non-AI) comparison passed overall.
    /// </summary>
    bool StructuralPassed,

    /// <summary>
    /// Average structural similarity across all compared pages (0.0–1.0).
    /// </summary>
    double StructuralSimilarity,

    /// <summary>
    /// Per-page structural comparison results.
    /// </summary>
    List<ImageComparisonResult> PageResults,

    /// <summary>
    /// Whether AI semantic comparison passed. Null if AI was unavailable.
    /// </summary>
    bool? AiSemanticPassed,

    /// <summary>
    /// Issues found by AI semantic comparison.
    /// </summary>
    List<string> AiIssues,

    /// <summary>
    /// When the comparison was performed.
    /// </summary>
    DateTimeOffset ComparedAt,

    /// <summary>
    /// Number of pages in the source PDF that were compared.
    /// </summary>
    int SourcePageCount,

    /// <summary>
    /// Number of pages rendered from the form definition.
    /// </summary>
    int RenderedPageCount);
