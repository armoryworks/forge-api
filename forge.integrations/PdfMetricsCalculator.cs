using Forge.Core.Models;

namespace Forge.Integrations;

/// <summary>
/// Computes CSS custom property values from PDF extraction data.
/// Returns a dictionary of key-value pairs ready to embed as formStyles
/// in the ComplianceFormDefinition JSON.
/// </summary>
public static class PdfMetricsCalculator
{
    /// <summary>
    /// CSS pixels per PDF point (96 DPI / 72 pts per inch).
    /// </summary>
    private const double PxPerPt = 96.0 / 72.0; // ≈ 1.333

    /// <summary>
    /// Analyze extracted PDF data and compute rendering metrics.
    /// Keys use CSS custom property names (without the -- prefix).
    /// All font sizes are output in CSS px, converted from PDF points
    /// at standard 96 DPI (1pt = 1.333px).
    /// </summary>
    public static Dictionary<string, string> Compute(PdfExtractionResult raw)
    {
        var metrics = new Dictionary<string, string>();
        if (raw.Pages.Count == 0) return metrics;

        var page1 = raw.Pages[0];
        var allTextItems = raw.Pages.SelectMany(p => p.TextItems).ToList();

        if (allTextItems.Count == 0) return metrics;

        // Convert PDF points to CSS pixels at 96 DPI (standard screen resolution).
        // This matches what the browser renders when viewing the source PDF at 100%.
        var renderScale = PxPerPt;

        // ─── Font tier analysis ───
        var fontBuckets = allTextItems
            .GroupBy(t => RoundToHalf(t.FontSize))
            .OrderByDescending(g => g.Count())
            .ToList();

        // Body font: most frequent size overall
        var bodyFontSize = fontBuckets.First().Key;
        metrics["gov-font"] = ToPx(bodyFontSize, renderScale);

        // Title font: largest font in top 15% of first page
        var topRegionY = page1.Height * 0.15;
        var titleItems = page1.TextItems
            .Where(t => t.Y <= topRegionY && t.FontSize > bodyFontSize)
            .OrderByDescending(t => t.FontSize)
            .ToList();
        if (titleItems.Count > 0)
        {
            metrics["gov-font-title"] = ToPx(titleItems[0].FontSize, renderScale);
        }

        // Step number font: font used for "Step N:" text patterns
        var stepItems = allTextItems
            .Where(t => t.Text.Trim().StartsWith("Step ", StringComparison.OrdinalIgnoreCase)
                        && t.IsBold)
            .ToList();
        if (stepItems.Count > 0)
        {
            var stepFontSize = RoundToHalf(stepItems
                .GroupBy(t => RoundToHalf(t.FontSize))
                .OrderByDescending(g => g.Count())
                .First().Key);
            metrics["gov-font-md"] = ToPx(stepFontSize, renderScale);
        }

        // Small font: most-used font size smaller than body (skip extremely tiny)
        var smallFonts = fontBuckets
            .Where(g => g.Key < bodyFontSize && g.Key >= bodyFontSize * 0.6)
            .OrderByDescending(g => g.Count())
            .ToList();
        if (smallFonts.Count > 0)
        {
            metrics["gov-font-sm"] = ToPx(smallFonts[0].Key, renderScale);
        }

        // Large font: largest size that is NOT the title and is within 2x body size
        var largeFonts = fontBuckets
            .Where(g => g.Key > bodyFontSize && g.Key <= bodyFontSize * 2.0)
            .OrderByDescending(g => g.Key)
            .ToList();
        if (largeFonts.Count > 0)
        {
            metrics["gov-font-lg"] = ToPx(largeFonts[0].Key, renderScale);
        }

        // ─── Line height ───
        var lineHeight = ComputeMedianLineHeight(page1.TextItems, bodyFontSize);
        if (lineHeight > 0)
        {
            metrics["gov-line-height"] = $"{lineHeight:F2}";
        }

        // ─── Step label column width ───
        var stepLabelPct = ComputeStepLabelWidth(page1);
        if (stepLabelPct > 0)
        {
            metrics["gov-step-label-pct"] = $"{stepLabelPct:F1}%";
        }

        // ─── Border weights ───
        metrics["gov-border-heavy"] = "2px";
        metrics["gov-border-normal"] = "1px";
        metrics["gov-border-color"] = "#000";

        return metrics;
    }

    /// <summary>
    /// Convert a PDF point size to CSS px at the given render scale, rounded to nearest integer.
    /// </summary>
    private static string ToPx(double pdfPt, double renderScale)
    {
        var px = Math.Round(pdfPt * renderScale);
        return $"{px}px";
    }

    /// <summary>
    /// Round a font size to the nearest 0.5pt for bucketing.
    /// </summary>
    private static double RoundToHalf(double value)
        => Math.Round(value * 2, MidpointRounding.AwayFromZero) / 2.0;

    /// <summary>
    /// Compute median line height ratio from adjacent text items at the body font size.
    /// </summary>
    private static double ComputeMedianLineHeight(List<PdfTextItem> items, double bodyFontSize)
    {
        var bodyItems = items
            .Where(t => Math.Abs(RoundToHalf(t.FontSize) - bodyFontSize) < 0.5)
            .OrderBy(t => t.Y)
            .ThenBy(t => t.X)
            .ToList();

        if (bodyItems.Count < 2) return 0;

        var ratios = new List<double>();
        for (int i = 1; i < bodyItems.Count; i++)
        {
            var dy = bodyItems[i].Y - bodyItems[i - 1].Y;
            // Only consider items that are roughly one line apart (not same line, not big gap)
            if (dy > bodyFontSize * 0.5 && dy < bodyFontSize * 3.0)
            {
                ratios.Add(dy / bodyFontSize);
            }
        }

        if (ratios.Count == 0) return 0;

        ratios.Sort();
        return ratios[ratios.Count / 2]; // median
    }

    /// <summary>
    /// Compute step label column width as percentage of page width.
    /// Finds "Step N:" text items and measures where the content starts to the right.
    /// </summary>
    private static double ComputeStepLabelWidth(PdfPageExtraction page)
    {
        var stepLabels = page.TextItems
            .Where(t => t.Text.Trim().StartsWith("Step ", StringComparison.OrdinalIgnoreCase)
                        && t.IsBold)
            .ToList();

        if (stepLabels.Count == 0) return 0;

        // Find the rightmost extent of step label text
        var maxRight = stepLabels.Max(t => t.X + t.Width);

        // Add a small margin (roughly the gap between label and content)
        var marginPx = page.Width * 0.01;
        var labelWidthPct = (maxRight + marginPx) / page.Width * 100;

        return Math.Round(labelWidthPct, 1);
    }
}
