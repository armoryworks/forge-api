using Microsoft.Extensions.Logging;

using SkiaSharp;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

/// <summary>
/// Structural pixel-level image comparison using SkiaSharp.
/// Computes SSIM-lite (block-based), content density delta, and region detection.
/// No AI dependency — purely algorithmic.
/// </summary>
public class SkiaImageComparisonService(ILogger<SkiaImageComparisonService> logger) : IImageComparisonService
{
    private const int BlockSize = 16;
    private const int DarkPixelThreshold = 128;
    private const double SimilarityPassThreshold = 0.55;
    private const double DensityDeltaFailThreshold = 0.3;
    private const double RegionCountTolerancePct = 0.30;

    public Task<ImageComparisonResult> CompareAsync(byte[] sourceImage, byte[] renderedImage, CancellationToken ct)
    {
        logger.LogInformation("Comparing images: source={SourceSize} bytes, rendered={RenderedSize} bytes",
            sourceImage.Length, renderedImage.Length);

        var issues = new List<string>();

        using var sourceBitmap = LoadAndConvertToGrayscale(sourceImage, "source");
        using var renderedBitmap = LoadAndConvertToGrayscale(renderedImage, "rendered");

        // Resize rendered to match source dimensions for fair comparison
        using var resizedRendered = ResizeToMatch(renderedBitmap, sourceBitmap.Width, sourceBitmap.Height);

        // 1. Content density (dark pixel ratio)
        var sourceDensity = ComputeContentDensity(sourceBitmap);
        var renderedDensity = ComputeContentDensity(resizedRendered);
        var densityDelta = Math.Abs(sourceDensity - renderedDensity);

        logger.LogDebug("Content density: source={Source:F3}, rendered={Rendered:F3}, delta={Delta:F3}",
            sourceDensity, renderedDensity, densityDelta);

        if (densityDelta >= DensityDeltaFailThreshold)
            issues.Add($"Content density difference too high ({densityDelta:F2}) — images have dramatically different content volume");

        // 2. Block-based SSIM-lite
        var similarity = ComputeBlockSsim(sourceBitmap, resizedRendered);
        logger.LogDebug("Block SSIM: {Similarity:F3}", similarity);

        if (similarity < SimilarityPassThreshold)
            issues.Add($"Structural similarity too low ({similarity:F2}) — layout differs significantly from source");

        // 3. Region detection (horizontal content bands)
        var sourceRegions = DetectRegions(sourceBitmap);
        var renderedRegions = DetectRegions(resizedRendered);

        logger.LogDebug("Regions: source={Source}, rendered={Rendered}", sourceRegions, renderedRegions);

        if (sourceRegions > 0)
        {
            var regionDiff = Math.Abs(sourceRegions - renderedRegions) / (double)sourceRegions;
            if (regionDiff > RegionCountTolerancePct)
                issues.Add($"Region count mismatch: source has {sourceRegions} bands, rendered has {renderedRegions} (>{RegionCountTolerancePct:P0} difference)");
        }

        // Composite pass/fail
        var passed = similarity >= SimilarityPassThreshold
                     && densityDelta < DensityDeltaFailThreshold
                     && (sourceRegions == 0 || Math.Abs(sourceRegions - renderedRegions) / (double)sourceRegions <= RegionCountTolerancePct);

        var result = new ImageComparisonResult(similarity, densityDelta, sourceRegions, renderedRegions, passed, issues);

        logger.LogInformation("Comparison result: similarity={Similarity:F3}, densityDelta={Delta:F3}, passed={Passed}",
            similarity, densityDelta, passed);

        return Task.FromResult(result);
    }

    private SKBitmap LoadAndConvertToGrayscale(byte[] imageBytes, string label)
    {
        using var original = SKBitmap.Decode(imageBytes)
                             ?? throw new InvalidOperationException($"Failed to decode {label} image ({imageBytes.Length} bytes)");

        var gray = new SKBitmap(original.Width, original.Height, SKColorType.Gray8, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(gray);
        using var paint = new SKPaint
        {
            ColorFilter = SKColorFilter.CreateColorMatrix(
            [
                0.299f, 0.587f, 0.114f, 0, 0,
                0.299f, 0.587f, 0.114f, 0, 0,
                0.299f, 0.587f, 0.114f, 0, 0,
                0, 0, 0, 1, 0,
            ]),
        };
        canvas.DrawBitmap(original, 0, 0, paint);

        logger.LogDebug("Loaded {Label} image: {Width}x{Height}", label, original.Width, original.Height);
        return gray;
    }

    private static SKBitmap ResizeToMatch(SKBitmap source, int targetWidth, int targetHeight)
    {
        if (source.Width == targetWidth && source.Height == targetHeight)
        {
            var copy = new SKBitmap(targetWidth, targetHeight, source.ColorType, source.AlphaType);
            using var canvas = new SKCanvas(copy);
            canvas.DrawBitmap(source, 0, 0);
            return copy;
        }

        var info = new SKImageInfo(targetWidth, targetHeight, source.ColorType, source.AlphaType);
        return source.Resize(info, SKSamplingOptions.Default);
    }

    private static double ComputeContentDensity(SKBitmap grayscale)
    {
        var pixels = grayscale.GetPixelSpan();
        var darkCount = 0;
        for (var i = 0; i < pixels.Length; i++)
        {
            if (pixels[i] < DarkPixelThreshold)
                darkCount++;
        }

        return (double)darkCount / pixels.Length;
    }

    /// <summary>
    /// Simplified SSIM computed over non-overlapping blocks.
    /// For each block, computes mean, variance, and covariance, then derives local SSIM.
    /// Returns the average across all blocks.
    /// </summary>
    private static double ComputeBlockSsim(SKBitmap source, SKBitmap rendered)
    {
        var width = Math.Min(source.Width, rendered.Width);
        var height = Math.Min(source.Height, rendered.Height);
        var sourceSpan = source.GetPixelSpan();
        var renderedSpan = rendered.GetPixelSpan();

        // SSIM constants (for 8-bit pixel values, L=255)
        const double k1 = 0.01, k2 = 0.03;
        const double l = 255.0;
        var c1 = (k1 * l) * (k1 * l);
        var c2 = (k2 * l) * (k2 * l);

        double totalSsim = 0;
        var blockCount = 0;

        for (var by = 0; by + BlockSize <= height; by += BlockSize)
        {
            for (var bx = 0; bx + BlockSize <= width; bx += BlockSize)
            {
                double sumS = 0, sumR = 0, sumSs = 0, sumRr = 0, sumSr = 0;
                var n = BlockSize * BlockSize;

                for (var dy = 0; dy < BlockSize; dy++)
                {
                    var row = (by + dy) * source.Width;
                    for (var dx = 0; dx < BlockSize; dx++)
                    {
                        var idx = row + bx + dx;
                        double s = idx < sourceSpan.Length ? sourceSpan[idx] : 0;
                        double r = idx < renderedSpan.Length ? renderedSpan[idx] : 0;

                        sumS += s;
                        sumR += r;
                        sumSs += s * s;
                        sumRr += r * r;
                        sumSr += s * r;
                    }
                }

                var meanS = sumS / n;
                var meanR = sumR / n;
                var varS = sumSs / n - meanS * meanS;
                var varR = sumRr / n - meanR * meanR;
                var covSr = sumSr / n - meanS * meanR;

                var ssim = (2 * meanS * meanR + c1) * (2 * covSr + c2)
                           / ((meanS * meanS + meanR * meanR + c1) * (varS + varR + c2));

                totalSsim += ssim;
                blockCount++;
            }
        }

        return blockCount > 0 ? totalSsim / blockCount : 0;
    }

    /// <summary>
    /// Detect horizontal content bands by projecting pixel darkness onto the Y-axis.
    /// A "band" is a contiguous run of rows with above-threshold darkness.
    /// </summary>
    private static int DetectRegions(SKBitmap grayscale)
    {
        var width = grayscale.Width;
        var height = grayscale.Height;
        var span = grayscale.GetPixelSpan();
        var threshold = width * 0.02; // A row is "content" if >2% of pixels are dark

        var inRegion = false;
        var regionCount = 0;

        for (var y = 0; y < height; y++)
        {
            var darkInRow = 0;
            for (var x = 0; x < width; x++)
            {
                if (span[y * width + x] < DarkPixelThreshold)
                    darkInRow++;
            }

            var isContent = darkInRow > threshold;
            if (isContent && !inRegion)
            {
                regionCount++;
                inRegion = true;
            }
            else if (!isContent)
            {
                inRegion = false;
            }
        }

        return regionCount;
    }
}
