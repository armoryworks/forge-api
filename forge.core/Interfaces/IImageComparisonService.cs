using Forge.Core.Models;

namespace Forge.Core.Interfaces;

/// <summary>
/// Structural pixel-level image comparison without AI dependency.
/// Compares a source PDF rendering against the Angular form rendering.
/// </summary>
public interface IImageComparisonService
{
    /// <summary>
    /// Compare two images structurally (SSIM-lite, content density, region detection).
    /// </summary>
    /// <param name="sourceImage">PNG bytes of the source PDF page rendering</param>
    /// <param name="renderedImage">PNG bytes of the Angular form rendering</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Comparison result with similarity metrics and pass/fail</returns>
    Task<ImageComparisonResult> CompareAsync(byte[] sourceImage, byte[] renderedImage, CancellationToken ct);
}
