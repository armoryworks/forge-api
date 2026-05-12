using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

/// <summary>
/// Mock image comparison for development/testing. Returns a passing result with 0.95 similarity.
/// </summary>
public class MockImageComparisonService(ILogger<MockImageComparisonService> logger) : IImageComparisonService
{
    public Task<ImageComparisonResult> CompareAsync(byte[] sourceImage, byte[] renderedImage, CancellationToken ct)
    {
        logger.LogInformation("[Mock] Comparing images: source={SourceSize} bytes, rendered={RenderedSize} bytes",
            sourceImage.Length, renderedImage.Length);

        var result = new ImageComparisonResult(
            StructuralSimilarity: 0.95,
            ContentDensityDelta: 0.02,
            SourceRegionCount: 8,
            RenderedRegionCount: 8,
            Passed: true,
            Issues: []);

        return Task.FromResult(result);
    }
}
