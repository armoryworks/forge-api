namespace Forge.Core.Models;

/// <summary>
/// Result of a structural pixel-level comparison between two images (source PDF vs rendered form).
/// </summary>
public record ImageComparisonResult(
    /// <summary>
    /// Block-based structural similarity (0.0–1.0). Computed as mean SSIM across 16x16 blocks.
    /// </summary>
    double StructuralSimilarity,

    /// <summary>
    /// Absolute difference in content density (dark pixel ratio) between images.
    /// Lower is better. Values > 0.3 indicate dramatically different content volume.
    /// </summary>
    double ContentDensityDelta,

    /// <summary>
    /// Number of horizontal content bands detected in the source image.
    /// </summary>
    int SourceRegionCount,

    /// <summary>
    /// Number of horizontal content bands detected in the rendered image.
    /// </summary>
    int RenderedRegionCount,

    /// <summary>
    /// Composite pass/fail: similarity >= 0.55, density delta < 0.3, region count within ±30%.
    /// </summary>
    bool Passed,

    /// <summary>
    /// Human-readable issues found during comparison.
    /// </summary>
    List<string> Issues);
