using System.Text.RegularExpressions;

using Forge.Core.Models;

namespace Forge.Integrations;

/// <summary>
/// Maps hardcoded field regions to real PDF annotation IDs from extraction data.
/// Supports positional (region-based) and name-based (regex on field name/ID) lookup.
/// </summary>
public static class AnnotationMapper
{
    /// <summary>
    /// Find a single annotation ID by approximate position on a page.
    /// </summary>
    public static string? FindByPosition(
        PdfExtractionResult result, int pageNumber,
        double xMin, double xMax, double yMin, double yMax,
        string? fieldType = null)
    {
        var ann = FindAnnotationsInRegion(result, pageNumber, xMin, xMax, yMin, yMax)
            .Where(a => fieldType is null || a.FieldType == fieldType)
            .OrderBy(a => a.Y).ThenBy(a => a.X)
            .FirstOrDefault();
        return ann?.Id;
    }

    /// <summary>
    /// Find a single annotation ID by regex match on the annotation's Id or FieldName.
    /// </summary>
    public static string? FindByName(
        PdfExtractionResult result, int pageNumber, string pattern)
    {
        var page = GetPage(result, pageNumber);
        if (page is null) return null;

        var regex = new Regex(pattern, RegexOptions.IgnoreCase);
        return page.Annotations
            .FirstOrDefault(a =>
                regex.IsMatch(a.Id) ||
                (a.FieldName is not null && regex.IsMatch(a.FieldName)))
            ?.Id;
    }

    /// <summary>
    /// Find all annotations within a rectangular region on a page.
    /// </summary>
    public static List<PdfAnnotationItem> FindAnnotationsInRegion(
        PdfExtractionResult result, int pageNumber,
        double xMin, double xMax, double yMin, double yMax)
    {
        var page = GetPage(result, pageNumber);
        if (page is null) return [];

        return page.Annotations
            .Where(a => a.X >= xMin && a.X <= xMax && a.Y >= yMin && a.Y <= yMax)
            .ToList();
    }

    /// <summary>
    /// Find all checkbox annotations in a region, returning their IDs and nearby label text.
    /// </summary>
    public static List<(string Id, string? Label)> FindCheckboxesInRegion(
        PdfExtractionResult result, int pageNumber,
        double xMin, double xMax, double yMin, double yMax)
    {
        var page = GetPage(result, pageNumber);
        if (page is null) return [];

        var checkboxes = page.Annotations
            .Where(a => a.FieldType == "checkbox" && a.X >= xMin && a.X <= xMax && a.Y >= yMin && a.Y <= yMax)
            .OrderBy(a => a.Y)
            .ToList();

        return checkboxes.Select(cb =>
        {
            // Find nearest text to the right of the checkbox
            var label = page.TextItems
                .Where(t => Math.Abs(t.Y - cb.Y) < 12 && t.X > cb.X && t.X < cb.X + 300)
                .OrderBy(t => t.X)
                .FirstOrDefault()?.Text;
            return (cb.Id, label);
        }).ToList();
    }

    /// <summary>
    /// Find all annotations on a page of a given type, ordered by position.
    /// </summary>
    public static List<PdfAnnotationItem> FindAllByType(
        PdfExtractionResult result, int pageNumber, string fieldType)
    {
        var page = GetPage(result, pageNumber);
        if (page is null) return [];

        return page.Annotations
            .Where(a => a.FieldType == fieldType)
            .OrderBy(a => a.Y).ThenBy(a => a.X)
            .ToList();
    }

    private static PdfPageExtraction? GetPage(PdfExtractionResult result, int pageNumber) =>
        result.Pages.FirstOrDefault(p => p.PageNumber == pageNumber);
}
