using System.Globalization;
using System.Text.RegularExpressions;

using Forge.Core.Interfaces.Communications;
using Forge.Core.Models.Extraction;

namespace Forge.Api.Features.Communications.Extraction;

/// <summary>
/// Pattern-matching extractor. The default implementation, and deliberately the
/// dullest one that works.
///
/// <para><b>Why start here rather than with a model.</b> Purchase orders are
/// filled with labelled fields — "PO Number:", "Qty", "Need by" — because they
/// are written to be read by a person in a hurry. Labelled text is exactly what
/// regexes are good at, and the failure mode is legible: it either matched or it
/// did not. An LLM's failure mode is a confident wrong number, which is the worst
/// possible outcome for a record whose entire purpose is proving what someone
/// actually asked for.</para>
///
/// <para>When a smarter extractor lands it replaces this one behind
/// <see cref="IOrderExtractor"/> with no pipeline change. That is the point of
/// the seam.</para>
/// </summary>
public partial class HeuristicOrderExtractor(ILogger<HeuristicOrderExtractor> logger) : IOrderExtractor
{
    public string ExtractorId => "heuristic-v1";

    public Task<ExtractionResult> ExtractAsync(ExtractionRequest request, CancellationToken ct)
    {
        var sources = (request.Sources ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Text))
            .ToList();

        if (sources.Count == 0)
            return Task.FromResult(ExtractionResult.Empty(ExtractorId, "No readable text in the message or its attachments."));

        var warnings = new List<string>();

        // Attachments first. A PO PDF is a document someone authored on purpose;
        // the email body around it is usually "see attached".
        var ordered = sources
            .OrderBy(s => s.Kind == ExtractionSourceKind.Attachment ? 0 : 1)
            .ToList();

        var poNumber = FindPoNumber(ordered, request.Subject, warnings);
        var needBy = FindNeedByDate(ordered, warnings);
        var lines = FindLines(ordered, warnings);

        var result = new ExtractionResult(
            lines,
            poNumber,
            needBy,
            warnings.Count == 0 ? null : warnings,
            ExtractorId);

        if (!result.FoundAnything)
        {
            logger.LogInformation(
                "[EXTRACT] {ExtractorId} found nothing across {Count} source(s); draft will be blank",
                ExtractorId, sources.Count);
        }

        return Task.FromResult(result);
    }

    // ── Customer PO number ──

    private static ExtractedField<string>? FindPoNumber(
        IReadOnlyList<ExtractionSource> sources, string? subject, List<string> warnings)
    {
        var found = new List<ExtractedField<string>>();

        foreach (var source in sources)
        {
            foreach (Match m in PoNumberLabelled().Matches(source.Text))
            {
                var value = m.Groups["po"].Value.Trim();
                if (value.Length is < 2 or > 50) continue;
                found.Add(new ExtractedField<string>(
                    value, ExtractionConfidence.High, m.Value.Trim(), source.ArtifactId));
            }
        }

        // The subject line is a common carrier ("PO 8832 for Acme") but is also
        // where forwarded and re-forwarded threads accumulate noise, so it ranks
        // below a labelled match in the document itself.
        if (found.Count == 0 && !string.IsNullOrWhiteSpace(subject))
        {
            var m = PoNumberLabelled().Match(subject);
            if (m.Success)
            {
                found.Add(new ExtractedField<string>(
                    m.Groups["po"].Value.Trim(), ExtractionConfidence.Medium, subject.Trim()));
            }
        }

        if (found.Count == 0) return null;

        // Conflicting numbers are reported, never silently resolved. A thread
        // that mentions two POs is exactly the case a human must look at.
        var distinct = found
            .Select(f => f.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinct.Count > 1)
        {
            warnings.Add(
                $"More than one purchase-order number appears in this message: {string.Join(", ", distinct)}. "
                + "The first is pre-filled; confirm which is correct.");
        }

        return found[0];
    }

    // ── Need-by date ──

    private static ExtractedField<DateTimeOffset>? FindNeedByDate(
        IReadOnlyList<ExtractionSource> sources, List<string> warnings)
    {
        foreach (var source in sources)
        {
            foreach (Match m in NeedByLabelled().Matches(source.Text))
            {
                var raw = m.Groups["date"].Value.Trim();
                if (TryParseDate(raw, out var parsed))
                {
                    return new ExtractedField<DateTimeOffset>(
                        parsed, ExtractionConfidence.High, m.Value.Trim(), source.ArtifactId);
                }

                warnings.Add($"Found a delivery date '{raw}' but could not read it as a date.");
            }
        }

        return null;
    }

    /// <summary>
    /// US-first date parsing, because the ambiguity is unavoidable and guessing
    /// silently is worse than picking a documented default. 03/04/2026 is March
    /// 4th here. ISO and month-name forms are unambiguous and parse exactly.
    /// </summary>
    private static bool TryParseDate(string raw, out DateTimeOffset value)
    {
        string[] formats =
        [
            "yyyy-MM-dd", "MM/dd/yyyy", "M/d/yyyy", "MM-dd-yyyy", "M-d-yyyy",
            "MMMM d, yyyy", "MMM d, yyyy", "d MMMM yyyy", "d MMM yyyy",
        ];

        if (DateTimeOffset.TryParseExact(raw, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value))
            return true;

        return DateTimeOffset.TryParse(raw, CultureInfo.GetCultureInfo("en-US"),
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value);
    }

    // ── Order lines ──

    private static IReadOnlyList<ExtractedOrderLine> FindLines(
        IReadOnlyList<ExtractionSource> sources, List<string> warnings)
    {
        var lines = new List<ExtractedOrderLine>();

        foreach (var source in sources)
        {
            foreach (var raw in source.Text.Split('\n'))
            {
                var text = raw.Trim();
                if (text.Length is 0 or > 400) continue;

                var m = QuantityPartLine().Match(text);
                if (!m.Success) continue;

                if (!decimal.TryParse(m.Groups["qty"].Value.Replace(",", ""),
                        NumberStyles.Any, CultureInfo.InvariantCulture, out var qty))
                    continue;

                if (qty <= 0) continue;

                var part = m.Groups["part"].Value.Trim().Trim('.', ',', ';', ':');
                if (string.IsNullOrWhiteSpace(part)) continue;

                var price = TryReadPrice(text, source.ArtifactId);

                lines.Add(new ExtractedOrderLine(
                    PartReference: new ExtractedField<string>(
                        part, ExtractionConfidence.Medium, text, source.ArtifactId),
                    Quantity: new ExtractedField<decimal>(
                        qty, ExtractionConfidence.Medium, text, source.ArtifactId),
                    UnitPrice: price,
                    Description: new ExtractedField<string>(
                        part, ExtractionConfidence.Low, text, source.ArtifactId)));
            }

            // First source that yields lines wins. Attachments are ordered
            // first, so a PO PDF's table beats the email body restating it —
            // otherwise the same order appears twice.
            if (lines.Count > 0) break;
        }

        if (lines.Count > 12)
        {
            warnings.Add(
                $"{lines.Count} candidate lines were read from this message. Check for text that "
                + "looks like a line item but is not.");
        }

        return lines;
    }

    private static ExtractedField<decimal>? TryReadPrice(string text, int? artifactId)
    {
        var m = UnitPrice().Match(text);
        if (!m.Success) return null;

        return decimal.TryParse(m.Groups["price"].Value.Replace(",", ""),
                NumberStyles.Any, CultureInfo.InvariantCulture, out var price) && price >= 0
            ? new ExtractedField<decimal>(price, ExtractionConfidence.Medium, m.Value.Trim(), artifactId)
            : null;
    }

    // ── Patterns ──
    // Source-generated so they compile once. Case-insensitive throughout because
    // nothing about a purchase order's capitalization is dependable.

    [GeneratedRegex(
        @"\b(?:customer\s+)?(?:p\.?\s?o\.?|purchase\s+order)\s*(?:#|no\.?|number|num)?\s*[:\-]?\s*(?<po>[A-Za-z0-9][A-Za-z0-9\-_/]{1,49})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PoNumberLabelled();

    [GeneratedRegex(
        @"\b(?:need(?:ed)?\s*(?:by|before)|due(?:\s*date)?|deliver(?:y)?\s*(?:by|date)|required\s*by|ship\s*by)\s*[:\-]?\s*(?<date>[A-Za-z0-9][A-Za-z0-9,\-/\s]{5,29})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NeedByLabelled();

    // "500 ea PN-1234", "500 x PN-1234", "Qty 500 PN-1234", "500 of PN-1234".
    // The optional label is "P/N" or "Part No." specifically — a bare "PN-"
    // is part of the part number itself and must not be stripped off it.
    // The part reference is required to contain a digit, which is what keeps
    // "3 more things" and "2 questions" out of the results.
    [GeneratedRegex(
        @"(?:^|\b)(?:qty\.?\s*[:\-]?\s*)?(?<qty>\d{1,3}(?:,\d{3})*(?:\.\d+)?)\s*(?:ea\.?|each|pcs?\.?|pieces?|units?|x|of)?\s+(?:(?:p/n|part\s*(?:no\.?|number|#))\s*[:\-]?\s*)?(?<part>(?=[A-Za-z0-9\-_/]*\d)[A-Za-z0-9][A-Za-z0-9\-_/]{2,49})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuantityPartLine();

    [GeneratedRegex(
        @"(?:@|at|unit\s*price|price\s*(?:ea\.?|each)?)\s*[:\-]?\s*\$?\s*(?<price>\d{1,3}(?:,\d{3})*(?:\.\d{1,4})?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnitPrice();
}
