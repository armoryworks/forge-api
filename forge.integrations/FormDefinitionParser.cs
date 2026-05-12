using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

/// <summary>
/// Converts raw pdf.js extraction data into ComplianceFormDefinition JSON.
/// Uses pattern detection to infer government form layout metadata.
/// </summary>
public partial class FormDefinitionParser(ILogger<FormDefinitionParser> logger) : IFormDefinitionParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // Regex patterns for government form structure detection
    [GeneratedRegex(@"^Step\s+(\d+)\s*[:\-\u2014.]?\s*(.*)", RegexOptions.IgnoreCase)]
    private static partial Regex StepPattern();

    [GeneratedRegex(@"^(Form|Department|Internal Revenue|OMB|Employer)", RegexOptions.IgnoreCase)]
    private static partial Regex FormHeaderPattern();

    [GeneratedRegex(@"^\d+\s*\([a-z]\)", RegexOptions.IgnoreCase)]
    private static partial Regex AmountLabelPattern();

    [GeneratedRegex(@"sign(ature|ed|ing|.here)", RegexOptions.IgnoreCase)]
    private static partial Regex SignaturePattern();

    [GeneratedRegex(@"(employ(er|ee)'?s?\s+(use|only|section|name))|(paid\s+preparer)", RegexOptions.IgnoreCase)]
    private static partial Regex EmployerSectionPattern();

    [GeneratedRegex(@"^employers?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex StandaloneEmployerPattern();

    public string Parse(PdfExtractionResult rawResult, string formType)
    {
        logger.LogInformation("Parsing {PageCount} pages into ComplianceFormDefinition (type={FormType})",
            rawResult.PageCount, formType);

        var isGovernment = DetectGovernmentLayout(rawResult);
        var pages = new List<object>();

        for (var pageIdx = 0; pageIdx < rawResult.Pages.Count; pageIdx++)
        {
            var rawPage = rawResult.Pages[pageIdx];
            var sections = isGovernment
                ? ParseGovernmentPage(rawPage, pageIdx)
                : ParseStandardPage(rawPage, pageIdx);

            // Detect read-only pages (no interactive fields)
            var hasInteractiveFields = sections.Any(s =>
                ((List<object>)((Dictionary<string, object?>)s)["fields"]!).Any(f =>
                {
                    var fd = (Dictionary<string, object?>)f;
                    var type = fd["type"]?.ToString();
                    return type is not "heading" and not "paragraph" and not "html";
                }));

            var page = new Dictionary<string, object?>
            {
                ["id"] = $"page{pageIdx + 1}",
                ["title"] = pageIdx == 0 ? "Page 1" : $"Page {pageIdx + 1}",
                ["sections"] = sections,
            };

            if (!hasInteractiveFields)
                page["readonly"] = true;

            pages.Add(page);
        }

        // Build form metadata from first page text
        var (title, formNumber, agency) = ExtractFormMetadata(rawResult);

        var definition = new Dictionary<string, object?>
        {
            ["formType"] = formType,
            ["title"] = title,
            ["formNumber"] = formNumber,
            ["revision"] = DateTimeOffset.UtcNow.ToString("yyyy-MM"),
            ["agency"] = agency,
            ["pages"] = pages,
        };

        if (isGovernment)
        {
            definition["formLayout"] = "government";
            definition["maxWidth"] = "850px";
        }

        // Compute rendering metrics from PDF extraction data
        var formStyles = PdfMetricsCalculator.Compute(rawResult);
        if (formStyles.Count > 0)
        {
            definition["formStyles"] = formStyles;
        }

        // Strip internal metadata fields (_y, _x) before serializing
        StripInternalMetadata(pages);

        var json = JsonSerializer.Serialize(definition, JsonOptions);

        logger.LogInformation("Parsed {SectionCount} sections across {PageCount} pages (government={IsGov})",
            pages.Sum(p => ((List<object>)((Dictionary<string, object?>)p)["sections"]!).Count),
            pages.Count, isGovernment);

        return json;
    }

    /// <summary>
    /// Detect whether this is a government form (IRS/state) by analyzing text patterns.
    /// </summary>
    private static bool DetectGovernmentLayout(PdfExtractionResult result)
    {
        if (result.Pages.Count == 0) return false;

        var firstPage = result.Pages[0];
        var topText = firstPage.TextItems
            .Where(t => t.Y < firstPage.Height * 0.15) // Top 15% of page
            .Select(t => t.Text)
            .ToList();

        var fullTopText = string.Join(" ", topText);

        // Look for government form indicators
        return fullTopText.Contains("Form W-", StringComparison.OrdinalIgnoreCase)
               || fullTopText.Contains("Internal Revenue", StringComparison.OrdinalIgnoreCase)
               || fullTopText.Contains("Department of the Treasury", StringComparison.OrdinalIgnoreCase)
               || fullTopText.Contains("Department of Homeland", StringComparison.OrdinalIgnoreCase)
               || fullTopText.Contains("OMB No.", StringComparison.OrdinalIgnoreCase)
               || fullTopText.Contains("Withholding Certificate", StringComparison.OrdinalIgnoreCase)
               || fullTopText.Contains("Employee's Withholding", StringComparison.OrdinalIgnoreCase)
               || StepPattern().IsMatch(string.Join(" ",
                   firstPage.TextItems.Where(t => t.IsBold).Select(t => t.Text)));
    }

    /// <summary>
    /// Parse a page from a government-style form (IRS W-4, state withholding, etc.).
    /// Detects step sections, form headers, amount lines, signature blocks.
    /// </summary>
    private List<object> ParseGovernmentPage(PdfPageExtraction page, int pageIdx)
    {
        var sections = new List<object>();

        // Sort text items by Y position (top to bottom), then X (left to right)
        var sortedText = page.TextItems
            .OrderBy(t => t.Y)
            .ThenBy(t => t.X)
            .ToList();

        // Detect section boundaries from bold/large text
        var boundaries = DetectSectionBoundaries(sortedText, page);

        if (boundaries.Count == 0)
        {
            // No clear sections detected — wrap everything in a single section
            sections.Add(BuildSection("section1", "Form Content", null, page.Annotations, sortedText, page));
            return sections;
        }

        // Assign text and fields to each section based on Y-position boundaries
        for (var i = 0; i < boundaries.Count; i++)
        {
            var boundary = boundaries[i];
            var nextY = i + 1 < boundaries.Count ? boundaries[i + 1].Y : page.Height;

            // Get text items within this section's Y range (extend slightly above for field labels)
            var sectionText = sortedText
                .Where(t => t.Y >= boundary.Y - 10 && t.Y < nextY)
                .Where(t => !IsPartOfBoundary(t, boundary)) // Exclude the header text itself
                .ToList();

            // Get annotations within this section's Y range
            // Use strict upper bound (nextY - 3) to avoid overlap when boundaries are close together
            var sectionAnnotations = page.Annotations
                .Where(a => a.Y >= boundary.Y - 10 && a.Y < nextY - 3)
                .ToList();

            var section = BuildGovernmentSection(boundary, sectionAnnotations, sectionText, page, i);
            sections.Add(section);
        }

        // Pick up any annotations not captured by sections (edge case)
        var capturedIds = sections.SelectMany(s =>
            ((List<object>)((Dictionary<string, object?>)s)["fields"]!)
                .Select(f => ((Dictionary<string, object?>)f)["id"]?.ToString()))
            .Where(id => id is not null)
            .ToHashSet();

        // Also exclude IDs that were intentionally converted (e.g., filing-status checkboxes → radio)
        foreach (var section in sections)
        {
            var sectionDict = (Dictionary<string, object?>)section;
            if (sectionDict.TryGetValue("_convertedIds", out var convertedObj) && convertedObj is HashSet<string> converted)
            {
                foreach (var id in converted)
                    capturedIds.Add(id);
                sectionDict.Remove("_convertedIds");
            }
        }

        var orphanedAnnotations = page.Annotations
            .Where(a => !capturedIds.Contains(a.Id))
            .ToList();

        if (orphanedAnnotations.Count > 0)
        {
            logger.LogWarning("Page {Page}: {Count} orphaned annotations not assigned to sections: {Ids}",
                page.PageNumber, orphanedAnnotations.Count,
                string.Join(", ", orphanedAnnotations.Select(a => a.Id)));

            // Add to the nearest section by Y-position
            foreach (var orphan in orphanedAnnotations)
            {
                var nearestIdx = FindNearestSectionIndex(sections, boundaries, orphan.Y);
                var nearestSection = (Dictionary<string, object?>)sections[nearestIdx];
                var fields = (List<object>)nearestSection["fields"]!;
                fields.Add(BuildField(orphan, page, sortedText));
            }
        }

        return sections;
    }

    /// <summary>
    /// Detect section boundaries from bold/large text items AND structural patterns.
    /// Returns sorted list of boundaries with section type classification.
    /// Many government PDFs (especially XFA forms like W-4) don't report bold flags via pdf.js,
    /// so we also scan ALL text for step/section patterns.
    /// </summary>
    private List<SectionBoundary> DetectSectionBoundaries(List<PdfTextItem> sortedText, PdfPageExtraction page)
    {
        var boundaries = new List<SectionBoundary>();

        // Find bold/large text that indicates section headers
        var headerCandidates = sortedText
            .Where(t => t.IsBold || t.FontSize >= 12)
            .OrderBy(t => t.Y)
            .ToList();

        // Check for form header at top of page 1
        if (page.PageNumber == 1)
        {
            var topText = sortedText.Where(t => t.Y < page.Height * 0.08).ToList();
            if (topText.Count > 0)
            {
                var headerText = string.Join(" ", topText.Select(t => t.Text));
                if (FormHeaderPattern().IsMatch(headerText) || topText.Any(t => t.FontSize >= 14))
                {
                    boundaries.Add(new SectionBoundary(
                        topText.Min(t => t.Y),
                        "form-header",
                        headerText,
                        null, null));
                }
            }
        }

        // Phase 1: scan header candidates (bold/large) for step patterns
        var processedYs = new HashSet<double>();
        ScanForSectionPatterns(headerCandidates, sortedText, boundaries, processedYs, page);

        // Phase 2: if no step boundaries found from bold text, scan ALL text for step patterns.
        // This handles XFA PDFs where bold flag is not reported.
        if (!boundaries.Any(b => b.SectionType == "step"))
        {
            logger.LogInformation("No step boundaries found from bold text — scanning all text for step patterns");
            // Look for text items that START with "Step N:" pattern (within left half of page)
            var stepCandidates = sortedText
                .Where(t => t.X < page.Width * 0.5 && StepPattern().IsMatch(t.Text))
                .OrderBy(t => t.Y)
                .ToList();

            ScanForSectionPatterns(stepCandidates, sortedText, boundaries, processedYs, page);
        }

        // Phase 3: detect signature and employer sections from all text
        // Always scan for employer sections — they can appear within a step section's Y range
        {
            var signCandidates = sortedText
                .Where(t => t.X < page.Width * 0.25
                    && (SignaturePattern().IsMatch(t.Text)
                        || EmployerSectionPattern().IsMatch(t.Text)
                        || StandaloneEmployerPattern().IsMatch(t.Text.Trim())))
                .OrderBy(t => t.Y)
                .ToList();

            foreach (var text in signCandidates)
            {
                if (processedYs.Any(y => Math.Abs(y - text.Y) < 5))
                    continue;

                var sameRowText = sortedText
                    .Where(t => Math.Abs(t.Y - text.Y) < 5)
                    .OrderBy(t => t.X)
                    .ToList();
                var combinedText = string.Join(" ", sameRowText.Select(t => t.Text));

                if (EmployerSectionPattern().IsMatch(text.Text) || EmployerSectionPattern().IsMatch(combinedText)
                    || StandaloneEmployerPattern().IsMatch(text.Text.Trim()))
                {
                    // For standalone "Employers" text, also check nearby lines for "Only"
                    var nearbyText = sortedText
                        .Where(t => Math.Abs(t.Y - text.Y) < 20 && t.X < page.Width * 0.25)
                        .OrderBy(t => t.Y)
                        .ToList();
                    var combined = string.Join(" ", nearbyText.Select(t => t.Text));
                    boundaries.Add(new SectionBoundary(text.Y, "employers-only", combined.Trim(), null, null));
                    processedYs.Add(text.Y);
                }
                else if (!boundaries.Any(b => b.SectionType == "sign")
                         && (SignaturePattern().IsMatch(text.Text) || SignaturePattern().IsMatch(combinedText)))
                {
                    // If this sign text is within a nearby step section (e.g., "Step 5: Sign Here"),
                    // mark that step as the sign section instead of creating a separate boundary
                    var nearbyStep = boundaries.FirstOrDefault(b =>
                        b.SectionType == "step" && text.Y - b.Y < 50 && text.Y > b.Y);
                    if (nearbyStep is not null)
                    {
                        // The step already contains the signature — don't create a separate boundary
                        processedYs.Add(text.Y);
                    }
                    else
                    {
                        boundaries.Add(new SectionBoundary(text.Y, "sign", combinedText.Trim(), null, null));
                        processedYs.Add(text.Y);
                    }
                }
            }
        }

        // Phase 4: detect form footer (small text at bottom of page)
        {
            var bottomY = page.Height * 0.92;
            var footerText = sortedText
                .Where(t => t.Y > bottomY && t.FontSize <= 8)
                .OrderBy(t => t.Y)
                .ToList();

            if (footerText.Count > 0 && !processedYs.Any(y => y > bottomY))
            {
                var combined = string.Join(" ", footerText.Select(t => t.Text));
                boundaries.Add(new SectionBoundary(footerText.Min(t => t.Y), "form-footer", combined.Trim(), null, null));
            }
        }

        return boundaries.OrderBy(b => b.Y).ToList();
    }

    /// <summary>
    /// Scan a list of candidate text items for step, signature, and employer section patterns.
    /// Adds discovered boundaries to the list. Skips already-processed Y positions.
    /// </summary>
    private static void ScanForSectionPatterns(
        List<PdfTextItem> candidates,
        List<PdfTextItem> allText,
        List<SectionBoundary> boundaries,
        HashSet<double> processedYs,
        PdfPageExtraction page)
    {
        foreach (var text in candidates)
        {
            // Skip items already captured as form header
            if (text.Y < page.Height * 0.08 && boundaries.Any(b => b.SectionType == "form-header"))
                continue;

            if (processedYs.Any(y => Math.Abs(y - text.Y) < 5))
                continue;

            // Concatenate text on the same Y row for pattern matching
            var sameRowText = allText
                .Where(t => Math.Abs(t.Y - text.Y) < 5)
                .OrderBy(t => t.X)
                .ToList();
            var combinedText = string.Join(" ", sameRowText.Select(t => t.Text));

            var stepMatch = StepPattern().Match(text.Text);
            if (!stepMatch.Success)
                stepMatch = StepPattern().Match(combinedText);

            if (stepMatch.Success)
            {
                var stepNum = stepMatch.Groups[1].Value;
                var stepName = stepMatch.Groups[2].Value.Trim();
                // If step name from the regex capture is empty, try the next same-font-size item
                // (must be same size — smaller text is instruction body, not a heading)
                if (string.IsNullOrWhiteSpace(stepName))
                {
                    // First try same row
                    var nextItem = sameRowText.FirstOrDefault(t =>
                        t.X > text.X + text.Width - 5
                        && Math.Abs(t.FontSize - text.FontSize) < 0.5);
                    if (nextItem is not null)
                        stepName = nextItem.Text.Trim();
                }
                // If still empty, look for multi-line step names at same X, same font size
                // (W-4 pattern: "Step 2:" then "Multiple Jobs" then "or Spouse Works" below)
                if (string.IsNullOrWhiteSpace(stepName))
                {
                    var belowLines = allText
                        .Where(t => t.Y > text.Y && t.Y < text.Y + 50
                                    && Math.Abs(t.X - text.X) < 10
                                    && Math.Abs(t.FontSize - text.FontSize) < 1)
                        .OrderBy(t => t.Y)
                        .Take(3) // Max 3 lines for step name
                        .ToList();
                    if (belowLines.Count > 0)
                        stepName = string.Join("\n", belowLines.Select(t => t.Text.Trim()));
                }
                // Drop step name if it's clearly instruction text (too long for a section title)
                if (stepName.Length > 60)
                    stepName = "";
                // Use just "Step N" as the section title
                var sectionTitle = $"Step {stepNum}" + (string.IsNullOrWhiteSpace(stepName) ? "" : $": {stepName}");
                boundaries.Add(new SectionBoundary(text.Y, "step", sectionTitle, stepNum, stepName));
                processedYs.Add(text.Y);
                continue;
            }

            if (SignaturePattern().IsMatch(text.Text) || SignaturePattern().IsMatch(combinedText))
            {
                boundaries.Add(new SectionBoundary(text.Y, "sign", combinedText.Trim(), null, null));
                processedYs.Add(text.Y);
                continue;
            }

            if (EmployerSectionPattern().IsMatch(text.Text) || EmployerSectionPattern().IsMatch(combinedText))
            {
                boundaries.Add(new SectionBoundary(text.Y, "employers-only", combinedText.Trim(), null, null));
                processedYs.Add(text.Y);
                continue;
            }

            // Large bold text that isn't a step — could be a section heading
            if (text.IsBold && text.FontSize >= 11 && text.Text.Length > 3
                && !boundaries.Any(b => Math.Abs(b.Y - text.Y) < 5))
            {
                boundaries.Add(new SectionBoundary(text.Y, "step", text.Text, null, null));
                processedYs.Add(text.Y);
            }
        }
    }

    /// <summary>
    /// Build a government-style section with layout metadata inferred from patterns.
    /// </summary>
    private Dictionary<string, object?> BuildGovernmentSection(
        SectionBoundary boundary,
        List<PdfAnnotationItem> annotations,
        List<PdfTextItem> sectionText,
        PdfPageExtraction page,
        int sectionIndex)
    {
        var fields = new List<object>();
        var section = new Dictionary<string, object?>
        {
            ["id"] = $"section{sectionIndex + 1}",
            ["title"] = boundary.Title.Trim(),
            ["fields"] = fields,
        };

        // Set layout based on section type
        switch (boundary.SectionType)
        {
            case "form-header":
                section["layout"] = "form-header";
                BuildFormHeaderFields(fields, sectionText, page);
                return section;

            case "step":
                section["layout"] = "step";
                if (boundary.StepNumber is not null)
                {
                    section["stepNumber"] = $"Step {boundary.StepNumber}:";
                    section["stepName"] = boundary.StepName ?? "";
                }
                // Detect sign steps (e.g., "Step 5: Sign Here")
                if (boundary.StepName?.Contains("Sign", StringComparison.OrdinalIgnoreCase) == true
                    || sectionText.Any(t => SignaturePattern().IsMatch(t.Text)))
                {
                    section["heavyBorder"] = true;
                }
                break;

            case "sign":
                section["layout"] = "step";
                section["heavyBorder"] = true;
                break;

            case "employers-only":
                section["layout"] = "employers-only";
                break;

            case "form-footer":
                section["layout"] = "form-footer";
                // Build footer as HTML spans
                var footerHtml = string.Join("  ", sectionText
                    .OrderBy(t => t.X)
                    .Select(t => t.Text));
                fields.Add(new Dictionary<string, object?>
                {
                    ["id"] = "footer_text",
                    ["type"] = "html",
                    ["label"] = "",
                    ["html"] = footerHtml,
                });
                return section;
        }

        // Add instruction text: non-bold text that is NOT a field label.
        // Two sources: (1) text immediately below the header before the first field
        // (pre-field instructions), and (2) text throughout the section that isn't
        // near any annotation (interleaved instructions between fields).
        // For the first "step" section on a page (index <= 1), skip pre-field text
        // to avoid capturing form preamble that sits in the header→Step 1 gap.
        var firstAnnotationY = annotations.Count > 0
            ? annotations.Min(a => a.Y)
            : boundary.Y + 200;
        var isFirstStep = sectionIndex <= 1 && boundary.SectionType == "step";

        var instructionItems = sectionText
            .Where(t => !t.IsBold && t.FontSize < 11 && t.Text.Length > 15)
            .Where(t => t.Y > boundary.Y + 5)
            .Where(t =>
                // Text before the first field (skip for first step — that's form preamble)
                (!isFirstStep && t.Y < firstAnnotationY - 5)
                // OR text throughout the section that's not near any field (interleaved instructions)
                // Skip for first step — the preamble text is scattered through Step 1
                || (!isFirstStep && !IsNearAnyAnnotation(t, annotations)))
            .OrderBy(t => t.Y).ThenBy(t => t.X)
            .Select(t => t.Text)
            .ToList();

        if (instructionItems.Count > 0)
        {
            section["instructions"] = string.Join(" ", instructionItems);
        }

        // Detect section sub-patterns
        var hasAmountFields = DetectAmountFields(annotations, page);
        var amountAnnotations = hasAmountFields
            ? annotations.Where(a => IsAmountPosition(a, page) && a.FieldType != "checkbox").ToList()
            : [];
        if (hasAmountFields)
        {
            section["layout"] = "step-amounts";
            section["amountColumnWidth"] = "155px";
        }

        // Detect shaded sections (even-numbered steps in W-4 pattern, or explicit detection)
        if (boundary.StepNumber is not null && int.TryParse(boundary.StepNumber, out var stepNum))
        {
            if (stepNum % 2 == 0) // Steps 2, 4 are typically shaded
                section["shaded"] = true;
        }

        // Build fields from annotations
        var radioGroups = new Dictionary<string, List<PdfAnnotationItem>>();
        foreach (var ann in annotations.OrderBy(a => a.Y).ThenBy(a => a.X))
        {
            // Group radio buttons by their group name
            if (ann.FieldType == "radio" && ann.RadioGroupName is not null)
            {
                if (!radioGroups.ContainsKey(ann.RadioGroupName))
                    radioGroups[ann.RadioGroupName] = [];
                radioGroups[ann.RadioGroupName].Add(ann);
                continue;
            }

            var field = BuildField(ann, page, sectionText);

            // Detect amount line pattern: number/currency field at right edge
            // Skip checkboxes — they can appear at right edge but aren't amounts
            if (hasAmountFields && IsAmountPosition(ann, page) && ann.FieldType != "checkbox")
            {
                field["fieldLayout"] = "amount-line";
                field["type"] = "currency";
                field["prefix"] = "$";

                // Try to find amount label from nearby text (e.g., "3(a)")
                var nearbyLabel = FindAmountLabel(ann, sectionText);
                if (nearbyLabel is not null)
                    field["amountLabel"] = nearbyLabel;

                // Find display text (instruction text before this field)
                var displayText = FindDisplayText(ann, sectionText, amountAnnotations);
                if (displayText is not null)
                    field["displayText"] = displayText;
            }

            // Detect grid cells (multiple fields on same Y-row)
            if (boundary.SectionType == "step" && !hasAmountFields)
            {
                var sameRowFields = annotations.Count(a =>
                    Math.Abs(a.Y - ann.Y) < 5 && a.Id != ann.Id);
                if (sameRowFields > 0)
                {
                    field["fieldLayout"] = "grid-cell";
                    var gridCol = GetGridColumn(ann, annotations, page);
                    if (gridCol is not null)
                        field["gridColumn"] = gridCol;
                }
            }

            // Detect signature fields
            if (IsSignatureField(ann, sectionText))
            {
                field["fieldLayout"] = "signature-field";
                field["type"] = "signature";
            }
            else if (IsSignatureDateField(ann, sectionText))
            {
                field["fieldLayout"] = "signature-date";
                field["type"] = "date";
            }

            // Detect filing status pattern (checkbox with descriptive text nearby)
            if (ann.FieldType == "checkbox" && HasFilingStatusContext(ann, sectionText))
            {
                field["fieldLayout"] = "checkbox-dots";
            }

            fields.Add(field);
        }

        // Build radio group fields
        foreach (var (groupName, radioButtons) in radioGroups)
        {
            var options = radioButtons
                .Select(r => new Dictionary<string, object?>
                {
                    ["value"] = r.DefaultValue ?? r.Id,
                    ["label"] = FindNearestLabel(r, sectionText) ?? r.DefaultValue ?? r.Id,
                })
                .ToList();

            var firstRadio = radioButtons[0];
            var radioField = new Dictionary<string, object?>
            {
                ["id"] = groupName,
                ["type"] = "radio",
                ["label"] = FindNearestLabel(firstRadio, sectionText) ?? groupName,
                ["required"] = firstRadio.Required,
                ["options"] = options,
            };

            // Detect filing status radio groups
            if (HasFilingStatusContext(firstRadio, sectionText))
                radioField["fieldLayout"] = "filing-status";

            fields.Add(radioField);
        }

        // Post-process: convert filing-status checkboxes into a single radio group
        var convertedIds = ConvertFilingStatusCheckboxesToRadio(fields, sectionText);
        if (convertedIds.Count > 0)
            section["_convertedIds"] = convertedIds;

        // Post-process: deduplicate amount labels (e.g., two "3(a)" → "3(a)" + "3(b)")
        DeduplicateAmountLabels(fields);

        // Post-process: move dollar amounts that leaked to adjacent fields back to their owners
        ReassignLeakedDollarAmounts(fields);

        // Post-process: shorten verbose display text on amount lines
        ShortenAmountDisplayText(fields);

        // Post-process: clean up checkbox labels (remove verbose instruction text)
        CleanCheckboxLabels(fields, boundary);

        // Post-process: set gridColumns on the section if grid-cell fields exist
        SetGridColumnsIfNeeded(section, fields);

        // Post-process: assign grid rows for multi-row grids
        AssignGridRows(fields);

        // Post-process: add synthetic signature + date fields for sign/step sections with no fields
        var isSignSection = boundary.SectionType == "sign"
            || (section.GetValueOrDefault("heavyBorder") is true);
        if (isSignSection && fields.Count == 0)
        {
            var hasSignatureText = sectionText.Any(t =>
                t.Text.Contains("signature", StringComparison.OrdinalIgnoreCase));
            var hasDateText = sectionText.Any(t =>
                t.Text.Trim().Equals("Date", StringComparison.OrdinalIgnoreCase));

            if (hasSignatureText)
            {
                fields.Add(new Dictionary<string, object?>
                {
                    ["id"] = "employeeSignature",
                    ["type"] = "signature",
                    ["label"] = "Employee\u2019s signature",
                    ["hint"] = "This form is not valid unless you sign it.",
                    ["fieldLayout"] = "signature-field",
                    ["autocomplete"] = "name",
                });
            }
            if (hasDateText)
            {
                fields.Add(new Dictionary<string, object?>
                {
                    ["id"] = "signatureDate",
                    ["type"] = "date",
                    ["label"] = "Date",
                    ["fieldLayout"] = "signature-date",
                });
            }
        }

        // Sort fields by position (top to bottom, left to right)
        var sortedFields = fields
            .OrderBy(f => GetFieldY(f))
            .ThenBy(f => GetFieldX(f))
            .ToList();
        fields.Clear();
        fields.AddRange(sortedFields);

        return section;
    }

    /// <summary>
    /// Build form header fields from text at top of page (3-column: left/center/right).
    /// Limits to the actual header area (top 12% of page) to avoid dumping body text into HTML blobs.
    /// </summary>
    private static void BuildFormHeaderFields(List<object> fields, List<PdfTextItem> headerText, PdfPageExtraction page)
    {
        // Defensive: only include text within the actual header area (top 12% of page),
        // regardless of how large the section's Y range is
        var maxHeaderY = page.Height * 0.12;
        var actualHeaderText = headerText.Where(t => t.Y < maxHeaderY).ToList();

        // If almost nothing is in the top 12%, widen to 18% — some forms have taller headers
        if (actualHeaderText.Count < 3)
            actualHeaderText = headerText.Where(t => t.Y < page.Height * 0.18).ToList();

        if (actualHeaderText.Count == 0)
            return;

        var leftThreshold = page.Width * 0.15;
        var rightThreshold = page.Width * 0.72;

        var leftItems = actualHeaderText.Where(t => t.X < leftThreshold).OrderBy(t => t.Y).ToList();
        var centerItems = actualHeaderText.Where(t => t.X >= leftThreshold && t.X < rightThreshold).OrderBy(t => t.Y).ToList();
        var rightItems = actualHeaderText.Where(t => t.X >= rightThreshold).OrderBy(t => t.Y).ToList();

        if (leftItems.Count > 0)
        {
            fields.Add(new Dictionary<string, object?>
            {
                ["id"] = "header_left",
                ["type"] = "html",
                ["label"] = "",
                ["html"] = string.Join("<br>", leftItems.Select(t => t.IsBold ? $"<strong>{t.Text}</strong>" : t.Text)),
                ["gridColumn"] = "left",
            });
        }

        if (centerItems.Count > 0)
        {
            var largest = centerItems.OrderByDescending(t => t.FontSize).First();
            fields.Add(new Dictionary<string, object?>
            {
                ["id"] = "header_center",
                ["type"] = "html",
                ["label"] = "",
                ["html"] = string.Join("<br>", centerItems.Select(t =>
                    t == largest ? $"<strong>{t.Text}</strong>" : t.Text)),
                ["gridColumn"] = "center",
            });
        }

        if (rightItems.Count > 0)
        {
            fields.Add(new Dictionary<string, object?>
            {
                ["id"] = "header_right",
                ["type"] = "html",
                ["label"] = "",
                ["html"] = string.Join("<br>", rightItems.Select(t => t.IsBold ? $"<strong>{t.Text}</strong>" : t.Text)),
                ["gridColumn"] = "right",
            });
        }

        // Add remaining section text (between header and next section) as instruction paragraph
        var belowHeaderText = headerText
            .Where(t => t.Y >= maxHeaderY && !t.IsBold && t.Text.Length > 15)
            .OrderBy(t => t.Y)
            .Select(t => t.Text)
            .ToList();

        if (belowHeaderText.Count > 0)
        {
            fields.Add(new Dictionary<string, object?>
            {
                ["id"] = "header_instructions",
                ["type"] = "paragraph",
                ["label"] = "",
                ["text"] = string.Join(" ", belowHeaderText),
            });
        }
    }

    /// <summary>
    /// Parse a standard (non-government) form page.
    /// </summary>
    private List<object> ParseStandardPage(PdfPageExtraction page, int pageIdx)
    {
        var sections = new List<object>();

        // Group text items into sections by detecting headings (bold + large)
        var headings = page.TextItems
            .Where(t => t.IsBold && t.FontSize >= 12)
            .OrderBy(t => t.Y)
            .ToList();

        if (headings.Count == 0)
        {
            // Single section containing all fields
            sections.Add(BuildSection($"section1", "Form Fields", null, page.Annotations, page.TextItems, page));
            return sections;
        }

        for (var i = 0; i < headings.Count; i++)
        {
            var heading = headings[i];
            var nextY = i + 1 < headings.Count ? headings[i + 1].Y : page.Height;

            var sectionAnnotations = page.Annotations
                .Where(a => a.Y >= heading.Y - 5 && a.Y < nextY + 5)
                .ToList();

            var sectionText = page.TextItems
                .Where(t => t.Y >= heading.Y && t.Y < nextY)
                .Where(t => t != heading)
                .ToList();

            sections.Add(BuildSection(
                $"section{i + 1}",
                heading.Text,
                sectionText.Where(t => !t.IsBold && t.Text.Length > 20).Select(t => t.Text).FirstOrDefault(),
                sectionAnnotations,
                sectionText,
                page));
        }

        return sections;
    }

    /// <summary>
    /// Build a standard section with fields from annotations.
    /// </summary>
    private Dictionary<string, object?> BuildSection(
        string id, string title, string? instructions,
        IEnumerable<PdfAnnotationItem> annotations,
        IEnumerable<PdfTextItem> textItems,
        PdfPageExtraction page)
    {
        var fields = new List<object>();
        var textList = textItems.ToList();
        var radioGroups = new Dictionary<string, List<PdfAnnotationItem>>();

        foreach (var ann in annotations.OrderBy(a => a.Y).ThenBy(a => a.X))
        {
            if (ann.FieldType == "radio" && ann.RadioGroupName is not null)
            {
                if (!radioGroups.ContainsKey(ann.RadioGroupName))
                    radioGroups[ann.RadioGroupName] = [];
                radioGroups[ann.RadioGroupName].Add(ann);
                continue;
            }

            fields.Add(BuildField(ann, page, textList));
        }

        // Build radio groups
        foreach (var (groupName, radios) in radioGroups)
        {
            var options = radios.Select(r => new Dictionary<string, object?>
            {
                ["value"] = r.DefaultValue ?? r.Id,
                ["label"] = FindNearestLabel(r, textList) ?? r.DefaultValue ?? r.Id,
            }).ToList();

            fields.Add(new Dictionary<string, object?>
            {
                ["id"] = groupName,
                ["type"] = "radio",
                ["label"] = FindNearestLabel(radios[0], textList) ?? groupName,
                ["required"] = radios[0].Required,
                ["options"] = options,
            });
        }

        var section = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["title"] = title,
            ["fields"] = fields,
        };

        if (instructions is not null)
            section["instructions"] = instructions;

        return section;
    }

    /// <summary>
    /// Build a single field from a PDF annotation, using nearby text items to resolve human-readable labels.
    /// </summary>
    private static Dictionary<string, object?> BuildField(PdfAnnotationItem ann, PdfPageExtraction page, List<PdfTextItem>? nearbyText = null)
    {
        var type = MapFieldType(ann);

        // Resolve human-readable label: prefer alt text (if not XFA path), then nearby text, then parsed XFA name
        var label = ResolveFieldLabel(ann, nearbyText);

        var field = new Dictionary<string, object?>
        {
            ["id"] = ann.Id,
            ["type"] = type,
            ["label"] = label,
            ["required"] = ann.Required ? true : null,
            ["_y"] = ann.Y,
            ["_x"] = ann.X,
        };

        if (ann.MaxLength.HasValue && ann.MaxLength > 0)
            field["maxlength"] = ann.MaxLength;

        if (ann.ReadOnly)
            field["readOnly"] = true;

        if (ann.DefaultValue is not null)
            field["defaultValue"] = ann.DefaultValue;

        if (ann.Options is { Count: > 0 } && type == "select")
        {
            field["options"] = ann.Options.Select(o => new Dictionary<string, object?>
            {
                ["value"] = o.Value,
                ["label"] = o.Label,
            }).ToList();
        }

        // Infer width from field position relative to page
        var relativeWidth = ann.Width / page.Width;
        field["width"] = relativeWidth switch
        {
            > 0.7 => "full",
            > 0.4 => "half",
            > 0.25 => "third",
            _ => "quarter",
        };

        return field;
    }

    /// <summary>
    /// Map PDF annotation field type to ComplianceFormDefinition field type.
    /// </summary>
    private static string MapFieldType(PdfAnnotationItem ann)
    {
        return ann.FieldType switch
        {
            "text" when ann.FieldName?.Contains("ssn", StringComparison.OrdinalIgnoreCase) == true
                         || ann.AlternativeText?.Contains("social security", StringComparison.OrdinalIgnoreCase) == true
                => "ssn",
            "text" when ann.FieldName?.Contains("date", StringComparison.OrdinalIgnoreCase) == true
                         || ann.AlternativeText?.Contains("date", StringComparison.OrdinalIgnoreCase) == true
                => "date",
            "text" when ann.FieldName?.Contains("signature", StringComparison.OrdinalIgnoreCase) == true
                         || ann.AlternativeText?.Contains("signature", StringComparison.OrdinalIgnoreCase) == true
                => "signature",
            "text" => "text",
            "checkbox" => "checkbox",
            "radio" => "radio",
            "select" or "listbox" => "select",
            "signature" => "signature",
            _ => "text",
        };
    }

    // ─── Label Resolution ─────────────────────────────────────────────────────────

    /// <summary>
    /// Resolve a human-readable label for a field annotation.
    /// Priority: (1) AlternativeText if not XFA path, (2) Nearest text from PDF,
    /// (3) Parsed human name from XFA path, (4) Raw ID as fallback.
    /// </summary>
    private static string ResolveFieldLabel(PdfAnnotationItem ann, List<PdfTextItem>? nearbyText)
    {
        // 1. Use AlternativeText if it's meaningful (not an XFA path)
        if (!string.IsNullOrEmpty(ann.AlternativeText) && !IsXfaPath(ann.AlternativeText))
            return ann.AlternativeText;

        // 2. Use FieldName if it's meaningful
        if (!string.IsNullOrEmpty(ann.FieldName) && !IsXfaPath(ann.FieldName))
            return ann.FieldName;

        // 3. Find nearest text label from the PDF content
        if (nearbyText is { Count: > 0 })
        {
            var textLabel = FindNearestLabel(ann, nearbyText);
            if (textLabel is not null && textLabel.Length >= 2)
                return textLabel;
        }

        // 4. Parse a readable name from XFA path (e.g., "f1_01" → "First name and middle initial")
        return ParseXfaFieldName(ann.Id);
    }

    [GeneratedRegex(@"(topmostSubform|Subform)\[")]
    private static partial Regex XfaPathPattern();

    private static bool IsXfaPath(string value) =>
        XfaPathPattern().IsMatch(value) || value.Contains("[0]");

    /// <summary>
    /// Extract a human-readable field name from an XFA-style ID.
    /// Falls back to a clean version of the last path segment.
    /// </summary>
    private static string ParseXfaFieldName(string xfaId)
    {
        // Extract the last meaningful segment: "topmostSubform[0].Page1[0].Step1a[0].f1_01[0]" → "f1_01"
        var segments = xfaId.Split('.');
        var lastSegment = segments.Length > 0 ? segments[^1] : xfaId;

        // Remove array indices: "f1_01[0]" → "f1_01"
        var cleanId = Regex.Replace(lastSegment, @"\[\d+\]", "");

        // Try to extract step/page context from the path
        var stepMatch = Regex.Match(xfaId, @"Step(\d+)([a-z])?", RegexOptions.IgnoreCase);
        var pageMatch = Regex.Match(xfaId, @"Page(\d+)", RegexOptions.IgnoreCase);

        var prefix = "";
        if (stepMatch.Success)
        {
            prefix = $"Step {stepMatch.Groups[1].Value}";
            if (stepMatch.Groups[2].Success)
                prefix += $"({stepMatch.Groups[2].Value})";
            prefix += " — ";
        }

        // Clean up the field name for display
        // "f1_01" → "Field 1", "c1_1" → "Check 1", etc.
        var fieldMatch = Regex.Match(cleanId, @"^([fc])(\d+)_(\d+)$");
        if (fieldMatch.Success)
        {
            var fieldType = fieldMatch.Groups[1].Value == "c" ? "Checkbox" : "Field";
            var fieldNum = fieldMatch.Groups[2].Value;
            var subNum = fieldMatch.Groups[3].Value;
            return $"{prefix}{fieldType} {fieldNum}.{subNum}";
        }

        return $"{prefix}{cleanId}";
    }

    // ─── Helper Methods ─────────────────────────────────────────────────────────

    private static bool DetectAmountFields(List<PdfAnnotationItem> annotations, PdfPageExtraction page)
    {
        // Amount fields are typically narrow number fields at the right edge of the page
        var rightEdge = page.Width * 0.65;
        return annotations.Count(a => a.X > rightEdge && a.Width < page.Width * 0.2) >= 2;
    }

    private static bool IsAmountPosition(PdfAnnotationItem ann, PdfPageExtraction page)
    {
        return ann.X > page.Width * 0.55 && ann.Width < page.Width * 0.25;
    }

    [GeneratedRegex(@"^\d+\s*\(?[a-z]?\)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ShortAmountLabelPattern();

    private static string? FindAmountLabel(PdfAnnotationItem ann, List<PdfTextItem> text)
    {
        // Look for short labels like "3(a)", "4(b)", "3", "4" near the amount field
        // Search both to the left and nearby above
        var nearbyLeft = text
            .Where(t => Math.Abs(t.Y - ann.Y) < 10 && t.X < ann.X
                && (AmountLabelPattern().IsMatch(t.Text.Trim()) || ShortAmountLabelPattern().IsMatch(t.Text.Trim())))
            .OrderByDescending(t => t.X)
            .FirstOrDefault();

        if (nearbyLeft is not null)
            return nearbyLeft.Text.Trim();

        // Also check if the field ID contains step/line info we can use
        var idMatch = Regex.Match(ann.Id, @"Step(\d+).*?f\d+_(\d+)", RegexOptions.IgnoreCase);
        if (idMatch.Success)
        {
            var step = idMatch.Groups[1].Value;
            var fieldNum = idMatch.Groups[2].Value;
            // Map common W-4 field numbers to labels
            return $"{step}";
        }

        return null;
    }

    private static string? FindDisplayText(PdfAnnotationItem ann, List<PdfTextItem> text,
        List<PdfAnnotationItem>? allAmountAnnotations = null)
    {
        // Compute adaptive Y range based on distance to nearest neighbor annotation.
        // For tightly-packed annotations, reduce the range to avoid cross-contamination.
        double yAbove = 20, yBelow = 15;
        if (allAmountAnnotations is { Count: > 1 })
        {
            var neighbors = allAmountAnnotations
                .Where(a => a.Id != ann.Id)
                .Select(a => a.Y - ann.Y)
                .ToList();

            var nearestAbove = neighbors.Where(d => d < 0).Select(d => -d).DefaultIfEmpty(999).Min();
            var nearestBelow = neighbors.Where(d => d > 0).DefaultIfEmpty(999).Min();

            // Only search halfway to the nearest neighbor (with minimum of 5px)
            yAbove = Math.Max(5, Math.Min(yAbove, nearestAbove / 2.0));
            yBelow = Math.Max(5, Math.Min(yBelow, nearestBelow / 2.0));
        }

        // Find instruction text on the same or preceding lines (to the left of the amount field).
        var candidates = text
            .Where(t => t.Y >= ann.Y - yAbove && t.Y <= ann.Y + yBelow
                        && t.X < ann.X - 10
                        && t.X > 90  // Skip step label column text (x < 90)
                        && !t.IsBold
                        && t.Text.Length >= 3
                        // Skip pure step headers
                        && !StepPattern().IsMatch(t.Text)
                        // Skip pure dots (leader dots)
                        && t.Text.Trim('.', ' ').Length > 0
                        // Skip standalone amount labels like "3(a)", "4", "$"
                        && !AmountLabelPattern().IsMatch(t.Text.Trim())
                        && t.Text.Trim() != "$"
                        // Skip standalone sub-item labels like "(a)", "(b)", "(c)"
                        && !Regex.IsMatch(t.Text.Trim(), @"^\([a-z]\)$"))
            .ToList();

        candidates = candidates
            .OrderBy(t => t.Y)
            .ThenBy(t => t.X)
            .ToList();

        if (candidates.Count == 0) return null;

        var result = string.Join(" ", candidates.Select(t => t.Text.Trim()));
        // Trim to reasonable length for display text
        if (result.Length > 200)
            result = result[..200] + "...";
        return result;
    }

    private static string? FindNearestLabel(PdfAnnotationItem ann, List<PdfTextItem> text)
    {
        // Find the nearest text item to the left of, above, or (for checkboxes) to the right of the annotation.
        // Skip step/section header text (e.g., "Step 1:") — not useful as field labels.
        var isCheckbox = ann.FieldType == "checkbox";

        var candidates = text
            .Where(t =>
                !StepPattern().IsMatch(t.Text) // Skip step headers
                && t.Text.Length >= 2          // Skip single characters
                && (
                    // Same line, to the left
                    (Math.Abs(t.Y - ann.Y) < 5 && t.X < ann.X && t.X + t.Width < ann.X + 10)
                    // Line above, roughly aligned
                    || (t.Y < ann.Y && ann.Y - t.Y < 15 && Math.Abs(t.X - ann.X) < 50)
                    // For checkboxes: text to the right on same line or slightly below
                    || (isCheckbox && t.X > ann.X + ann.Width - 5 && Math.Abs(t.Y - ann.Y) < 12
                        && t.X < ann.X + 200)))
            .OrderBy(t =>
            {
                var yDist = Math.Abs(t.Y - ann.Y);
                var xDist = Math.Abs(t.X - ann.X);
                // For checkboxes, prefer text to the right (lower x-penalty)
                if (isCheckbox && t.X > ann.X)
                    return yDist + xDist * 0.2;
                return yDist + xDist * 0.5;
            })
            .FirstOrDefault();

        if (candidates is null) return null;

        // If the label is very short (form reference like "(a)", "(b)", "(c)"),
        // concatenate with the descriptive text that follows on the same line
        if (candidates.Text.Length <= 4)
        {
            var followingText = text
                .Where(t => Math.Abs(t.Y - candidates.Y) < 3
                    && t.X > candidates.X + candidates.Width - 2
                    && t.X < candidates.X + candidates.Width + 20
                    && t.Text.Length > 3)
                .OrderBy(t => t.X)
                .FirstOrDefault();

            if (followingText is not null)
                return $"{candidates.Text} {followingText.Text}";
        }

        return candidates.Text;
    }

    private static string? GetGridColumn(PdfAnnotationItem ann, List<PdfAnnotationItem> allAnnotations, PdfPageExtraction page)
    {
        // Determine grid column based on X position relative to page width
        var relX = ann.X / page.Width;
        var sameRow = allAnnotations
            .Where(a => Math.Abs(a.Y - ann.Y) < 5)
            .OrderBy(a => a.X)
            .ToList();

        if (sameRow.Count <= 1) return null;

        var colIdx = sameRow.IndexOf(ann) + 1;
        var totalCols = sameRow.Count;

        // Wide fields span multiple columns
        var relWidth = ann.Width / page.Width;
        if (relWidth > 0.5)
            return $"1 / {totalCols}";

        return colIdx.ToString();
    }

    private static bool IsSignatureField(PdfAnnotationItem ann, List<PdfTextItem> text)
    {
        var nearby = text
            .Where(t => Math.Abs(t.Y - ann.Y) < 15 && Math.Abs(t.X - ann.X) < 50)
            .Any(t => SignaturePattern().IsMatch(t.Text));

        return nearby || ann.AlternativeText?.Contains("signature", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsSignatureDateField(PdfAnnotationItem ann, List<PdfTextItem> text)
    {
        // Date field near a signature field
        var isDateField = ann.FieldName?.Contains("date", StringComparison.OrdinalIgnoreCase) == true
                          || ann.AlternativeText?.Contains("date", StringComparison.OrdinalIgnoreCase) == true;

        if (!isDateField) return false;

        // Check if there's a signature-related text nearby
        return text.Any(t => Math.Abs(t.Y - ann.Y) < 30 && SignaturePattern().IsMatch(t.Text));
    }

    private static bool HasFilingStatusContext(PdfAnnotationItem ann, List<PdfTextItem> text)
    {
        return text.Any(t =>
            Math.Abs(t.Y - ann.Y) < 20
            && (t.Text.Contains("filing status", StringComparison.OrdinalIgnoreCase)
                || t.Text.Contains("Single", StringComparison.OrdinalIgnoreCase)
                || t.Text.Contains("Married", StringComparison.OrdinalIgnoreCase)
                || t.Text.Contains("Head of household", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Returns true if a text item is near any annotation — meaning it's likely a field label,
    /// not section instruction text.
    /// </summary>
    private static bool IsNearAnyAnnotation(PdfTextItem text, List<PdfAnnotationItem> annotations)
    {
        return annotations.Any(a =>
            Math.Abs(a.Y - text.Y) < 20
            && Math.Abs(a.X - text.X) < 250);
    }

    private static bool IsPartOfBoundary(PdfTextItem text, SectionBoundary boundary)
    {
        // Exclude text that was used to BUILD the boundary title (same Y, contained in title).
        // But only for text items that are clearly section header text (left margin, not field labels).
        // Field labels like "Employer's name and address" should NOT be excluded.
        if (Math.Abs(text.Y - boundary.Y) > 3)
            return false;
        if (!boundary.Title.Contains(text.Text, StringComparison.OrdinalIgnoreCase))
            return false;
        // Only exclude if the text is in the left header area (X < 15% of a typical 612-wide page)
        // Field labels are typically further right (X > 90)
        return text.X < 90;
    }

    private static int FindNearestSectionIndex(
        List<object> sections, List<SectionBoundary> boundaries, double y)
    {
        var minDist = double.MaxValue;
        var minIdx = 0;
        for (var i = 0; i < boundaries.Count; i++)
        {
            var dist = Math.Abs(boundaries[i].Y - y);
            if (dist < minDist)
            {
                minDist = dist;
                minIdx = i;
            }
        }
        return Math.Min(minIdx, sections.Count - 1);
    }

    private static (string title, string formNumber, string agency) ExtractFormMetadata(PdfExtractionResult result)
    {
        if (result.Pages.Count == 0)
            return ("Unknown Form", "Unknown", "Unknown");

        var firstPage = result.Pages[0];
        var topItems = firstPage.TextItems
            .Where(t => t.Y < firstPage.Height * 0.1)
            .OrderBy(t => t.Y)
            .ThenBy(t => t.X)
            .ToList();

        // Find the largest text as title
        var titleItem = topItems.OrderByDescending(t => t.FontSize).FirstOrDefault();
        var title = titleItem?.Text ?? "Unknown Form";

        // Find form number (e.g., "Form W-4", "Form I-9")
        var formNumberItem = topItems.FirstOrDefault(t =>
            t.Text.StartsWith("Form", StringComparison.OrdinalIgnoreCase));
        var formNumber = formNumberItem?.Text ?? "Unknown";

        // Find agency
        var agencyItem = topItems.FirstOrDefault(t =>
            t.Text.Contains("Department", StringComparison.OrdinalIgnoreCase)
            || t.Text.Contains("Internal Revenue", StringComparison.OrdinalIgnoreCase));
        var agency = agencyItem?.Text ?? "Unknown";

        return (title, formNumber, agency);
    }

    /// <summary>
    /// Convert filing-status checkbox fields into a single radio-like field.
    /// W-4 checkboxes c1_1[0], c1_1[1], c1_1[2] become a single filing-status select.
    /// </summary>
    private static HashSet<string> ConvertFilingStatusCheckboxesToRadio(List<object> fields, List<PdfTextItem> sectionText)
    {
        var removedIds = new HashSet<string>();

        // Find ALL checkboxes in this section (filing status or checkbox-dots)
        var allCheckboxes = fields
            .Where(f =>
            {
                var fd = (Dictionary<string, object?>)f;
                return fd["type"]?.ToString() == "checkbox";
            })
            .ToList();

        if (allCheckboxes.Count < 2) return removedIds;

        // Check that these are actually filing status checkboxes
        var checkboxLabels = allCheckboxes.Select(f =>
        {
            var fd = (Dictionary<string, object?>)f;
            return fd["label"]?.ToString() ?? "";
        }).ToList();

        var isFilingStatus = checkboxLabels.Any(l =>
            l.Contains("Single", StringComparison.OrdinalIgnoreCase)
            || l.Contains("Married", StringComparison.OrdinalIgnoreCase)
            || l.Contains("Head of household", StringComparison.OrdinalIgnoreCase));

        // Also check nearby text for filing status context
        if (!isFilingStatus)
        {
            isFilingStatus = sectionText.Any(t =>
                t.Text.Contains("Single", StringComparison.OrdinalIgnoreCase)
                || t.Text.Contains("Married", StringComparison.OrdinalIgnoreCase)
                || t.Text.Contains("filing status", StringComparison.OrdinalIgnoreCase));
        }

        if (!isFilingStatus) return removedIds;

        // Use all checkboxes that look like filing status (same ID prefix or near each other)
        var filingCheckboxes = allCheckboxes;

        // Build a radio group from the checkboxes
        var options = new List<Dictionary<string, object?>>();
        var filingStatusLabels = new[]
        {
            ("Single", "Single or Married filing separately"),
            ("Married", "Married filing jointly or Qualifying surviving spouse"),
            ("HeadOfHousehold", "Head of household (Check only if you\u2019re unmarried and pay more than half the costs of keeping up a home for yourself and a qualifying individual.)"),
        };

        for (var i = 0; i < filingCheckboxes.Count && i < filingStatusLabels.Length; i++)
        {
            options.Add(new Dictionary<string, object?>
            {
                ["value"] = filingStatusLabels[i].Item1,
                ["label"] = filingStatusLabels[i].Item2,
            });
        }

        var firstCheckbox = (Dictionary<string, object?>)filingCheckboxes[0];

        var radioField = new Dictionary<string, object?>
        {
            ["id"] = "filingStatus",
            ["type"] = "radio",
            ["label"] = "(c) Filing status",
            ["fieldLayout"] = "filing-status",
            ["options"] = options,
            ["_y"] = firstCheckbox.GetValueOrDefault("_y"),
            ["_x"] = firstCheckbox.GetValueOrDefault("_x"),
        };

        // Remove original checkboxes and add the radio group
        var checkboxIds = new HashSet<string>();
        foreach (var cb in filingCheckboxes)
        {
            var cbDict = (Dictionary<string, object?>)cb;
            var cbId = cbDict["id"]?.ToString() ?? "";
            checkboxIds.Add(cbId);
        }

        // Remove by rebuilding the list without the checkboxes
        var toKeep = new List<object>();
        foreach (var f in fields)
        {
            var fd = (Dictionary<string, object?>)f;
            var fId = fd["id"]?.ToString() ?? "";
            if (!checkboxIds.Contains(fId))
                toKeep.Add(f);
        }
        fields.Clear();
        fields.AddRange(toKeep);
        fields.Add(radioField);

        return checkboxIds;
    }

    /// <summary>
    /// When PDF text wraps, dollar amounts from one field's description can end up
    /// in the next field's display text. Move them back: if field[i] displayText ends
    /// with "by" and field[i+1] starts with "$X,XXX", append the amount to field[i].
    /// </summary>
    private static void ReassignLeakedDollarAmounts(List<object> fields)
    {
        var amountFields = fields
            .Select(f => (Dictionary<string, object?>)f)
            .Where(f => f.GetValueOrDefault("fieldLayout")?.ToString()?.StartsWith("amount-line") == true
                        && f.ContainsKey("displayText"))
            .ToList();

        for (var i = 0; i < amountFields.Count - 1; i++)
        {
            var current = amountFields[i];
            var next = amountFields[i + 1];

            var currentText = current["displayText"]?.ToString() ?? "";
            var nextText = next["displayText"]?.ToString() ?? "";

            // Check if current ends with a preposition and next starts with $
            if (currentText.TrimEnd().EndsWith(" by", StringComparison.OrdinalIgnoreCase)
                && Regex.IsMatch(nextText, @"^\$[\d,.]+"))
            {
                // Extract the dollar amount from next
                var match = Regex.Match(nextText, @"^(\$[\d,.]+)\s*\.?\s*");
                if (match.Success)
                {
                    current["displayText"] = currentText.TrimEnd() + " " + match.Groups[1].Value;
                    next["displayText"] = nextText[match.Length..].TrimStart();
                }
            }
        }
    }

    /// <summary>
    /// Deduplicate amount labels within a section. If two fields have the same amountLabel
    /// (e.g., both "3(a)"), assign sequential sub-letters: "3(a)", "3(b)", etc.
    /// </summary>
    private static void DeduplicateAmountLabels(List<object> fields)
    {
        var amountFields = fields
            .Select(f => (Dictionary<string, object?>)f)
            .Where(f => f.GetValueOrDefault("fieldLayout")?.ToString() == "amount-line"
                        && f.ContainsKey("amountLabel"))
            .OrderBy(f => double.TryParse(f.GetValueOrDefault("_y")?.ToString(), out var y) ? y : 0)
            .ToList();

        if (amountFields.Count < 2) return;

        // Group by base number (e.g., "3" from "3(a)")
        var baseGroups = new Dictionary<string, List<Dictionary<string, object?>>>();
        foreach (var field in amountFields)
        {
            var label = field["amountLabel"]?.ToString() ?? "";
            // Extract base number: "3(a)" → "3", "4(b)" → "4", "3" → "3"
            var baseMatch = Regex.Match(label, @"^(\d+)");
            var baseNum = baseMatch.Success ? baseMatch.Groups[1].Value : label;

            if (!baseGroups.ContainsKey(baseNum))
                baseGroups[baseNum] = [];
            baseGroups[baseNum].Add(field);
        }

        // For each base group with multiple fields, assign sequential sub-labels
        foreach (var (baseNum, groupFields) in baseGroups)
        {
            if (groupFields.Count <= 1) continue;

            // Check if the last field is a total (no sub-letter) — keep it as just the number
            var lastField = groupFields[^1];
            var lastLabel = lastField["amountLabel"]?.ToString() ?? "";
            var lastIsTotal = !lastLabel.Contains('(');

            var subLetterFields = lastIsTotal ? groupFields[..^1] : groupFields;

            for (var i = 0; i < subLetterFields.Count; i++)
            {
                var subLetter = (char)('a' + i);
                subLetterFields[i]["amountLabel"] = $"{baseNum}({subLetter})";
            }
        }
    }

    /// <summary>
    /// Shorten verbose display text on amount-line fields.
    /// Government form instructions are often full paragraphs — trim to first clause.
    /// </summary>
    private static void ShortenAmountDisplayText(List<object> fields)
    {
        foreach (var field in fields.Select(f => (Dictionary<string, object?>)f))
        {
            if (field.GetValueOrDefault("fieldLayout")?.ToString() != "amount-line") continue;

            var displayText = field.GetValueOrDefault("displayText")?.ToString();
            if (displayText is null) continue;

            // Clean up leading fragments (text that starts mid-sentence from PDF wrapping)
            var cleaned = displayText;

            // Strip leading dollar amount fragments from previous field's wrapped text
            // e.g., "$2,200 . Multiply the number..." → "Multiply the number..."
            var dollarLeadMatch = Regex.Match(cleaned, @"^\$[\d,.]+\s*\.?\s*");
            if (dollarLeadMatch.Success)
                cleaned = cleaned[dollarLeadMatch.Length..].TrimStart();

            // Strip leading fragments from previous field's continuation text.
            if (cleaned.Length > 0 && char.IsLower(cleaned[0]))
            {
                // Text starts lowercase — it's a continuation from a previous field's wrapped text.
                // Find where THIS field's actual content starts (first capital letter after ". " or "): ")
                var newStart = Regex.Match(cleaned, @"[.)]:?\s+([A-Z])");
                if (newStart.Success && newStart.Index < cleaned.Length * 0.7)
                {
                    cleaned = cleaned[(newStart.Index + newStart.Length - 1)..];
                }
            }

            // Strip generic trailing instruction fragments that leaked from an adjacent field
            // e.g., "Enter the result here Extra withholding." → "Extra withholding."
            if (cleaned.Length > 0)
            {
                var hereIdx = cleaned.IndexOf(" here ", 0, Math.Min(40, cleaned.Length), StringComparison.OrdinalIgnoreCase);
                if (hereIdx >= 5)
                {
                    var afterHere = hereIdx + 6; // " here " is 6 chars
                    if (afterHere < cleaned.Length && char.IsUpper(cleaned[afterHere]))
                    {
                        cleaned = cleaned[afterHere..];
                    }
                }
            }

            if (cleaned.Length <= 100)
            {
                field["displayText"] = cleaned;
                continue;
            }

            // Try to find a natural break point
            var shortened = cleaned;

            // Cut at first period that's followed by a space (end of sentence)
            var periodIdx = cleaned.IndexOf(". ", StringComparison.Ordinal);
            if (periodIdx > 15 && periodIdx < 120)
            {
                shortened = cleaned[..(periodIdx + 1)];
            }
            else
            {
                // Cut at comma or semicolon after reasonable content
                var commaIdx = cleaned.IndexOf(", ", 30, StringComparison.Ordinal);
                if (commaIdx > 15 && commaIdx < 100)
                {
                    shortened = cleaned[..commaIdx];
                }
                else if (cleaned.Length > 100)
                {
                    // Hard truncate at word boundary
                    var cutAt = cleaned.LastIndexOf(' ', Math.Min(100, cleaned.Length - 1));
                    if (cutAt > 20)
                        shortened = cleaned[..cutAt] + "\u2026";
                    else
                        shortened = cleaned[..100] + "\u2026";
                }
            }

            field["displayText"] = shortened;
        }
    }

    /// <summary>
    /// Clean up checkbox labels that contain verbose instruction text.
    /// For step sections, replace long labels with a concise description.
    /// </summary>
    private static void CleanCheckboxLabels(List<object> fields, SectionBoundary boundary)
    {
        foreach (var field in fields.Select(f => (Dictionary<string, object?>)f))
        {
            if (field.GetValueOrDefault("type")?.ToString() != "checkbox") continue;

            var label = field.GetValueOrDefault("label")?.ToString() ?? "";

            // For Step 2 W-4: the checkbox means "Two jobs or spouse also works"
            if (boundary.StepNumber == "2")
            {
                field["label"] = "Complete Steps 2\u20134 only if they apply to you";
                continue;
            }

            // For exemption checkboxes (any length)
            if (label.Contains("exemption", StringComparison.OrdinalIgnoreCase)
                || label.Contains("exempt", StringComparison.OrdinalIgnoreCase)
                || label.Contains("conditions for exempt", StringComparison.OrdinalIgnoreCase))
            {
                field["label"] = "Claim exemption from withholding";
                continue;
            }

            // If label is verbose instruction text (> 50 chars), shorten it
            if (label.Length > 50)
            {
                var cutAt = label.LastIndexOf(' ', Math.Min(50, label.Length - 1));
                if (cutAt > 15)
                    field["label"] = label[..cutAt];
            }
        }
    }

    /// <summary>
    /// Set gridColumns on the section if it contains grid-cell fields.
    /// This tells the renderer to render a CSS Grid layout.
    /// </summary>
    private static void SetGridColumnsIfNeeded(Dictionary<string, object?> section, List<object> fields)
    {
        var gridCells = fields
            .Where(f => ((Dictionary<string, object?>)f).GetValueOrDefault("fieldLayout")?.ToString() == "grid-cell")
            .ToList();

        if (gridCells.Count < 2) return;

        // Find the max column number
        var maxCol = gridCells
            .Select(f =>
            {
                var colStr = ((Dictionary<string, object?>)f).GetValueOrDefault("gridColumn")?.ToString();
                return int.TryParse(colStr, out var col) ? col : 1;
            })
            .Max();

        // Build grid template: e.g., "1fr 1fr 200px" for 3 columns (last is SSN)
        var cols = Enumerable.Repeat("1fr", Math.Max(maxCol - 1, 1)).ToList();
        cols.Add("200px"); // Last column is typically SSN or narrow
        section["gridColumns"] = string.Join(" ", cols);
    }

    /// <summary>
    /// Assign grid rows to grid-cell fields based on their Y positions.
    /// Fields at similar Y become same row.
    /// </summary>
    private static void AssignGridRows(List<object> fields)
    {
        var gridCells = fields
            .Where(f => ((Dictionary<string, object?>)f).GetValueOrDefault("fieldLayout")?.ToString() == "grid-cell")
            .Select(f => (Dictionary<string, object?>)f)
            .OrderBy(f => double.TryParse(f.GetValueOrDefault("_y")?.ToString(), out var y) ? y : 0)
            .ToList();

        if (gridCells.Count < 2) return;

        var currentRow = 1;
        double? lastY = null;

        foreach (var cell in gridCells)
        {
            var y = double.TryParse(cell.GetValueOrDefault("_y")?.ToString(), out var py) ? py : 0;
            if (lastY.HasValue && Math.Abs(y - lastY.Value) > 10)
                currentRow++;
            cell["gridRow"] = currentRow.ToString();
            lastY = y;
        }
    }

    private static double GetFieldY(object field) =>
        double.TryParse(((Dictionary<string, object?>)field).GetValueOrDefault("_y")?.ToString(), out var y) ? y : 0;

    private static double GetFieldX(object field) =>
        double.TryParse(((Dictionary<string, object?>)field).GetValueOrDefault("_x")?.ToString(), out var x) ? x : 0;

    /// <summary>
    /// Strip internal metadata fields (_y, _x) that are used for sorting/processing
    /// but should not appear in the final JSON output.
    /// </summary>
    private static void StripInternalMetadata(List<object> pages)
    {
        foreach (var page in pages)
        {
            var pageDict = (Dictionary<string, object?>)page;
            var sections = (List<object>)pageDict["sections"]!;
            foreach (var section in sections)
            {
                var sectionDict = (Dictionary<string, object?>)section;
                var fields = (List<object>)sectionDict["fields"]!;
                foreach (var field in fields)
                {
                    var fieldDict = (Dictionary<string, object?>)field;
                    fieldDict.Remove("_y");
                    fieldDict.Remove("_x");
                }
            }
        }
    }

    // ─── Internal Types ─────────────────────────────────────────────────────────

    private record SectionBoundary(
        double Y,
        string SectionType,
        string Title,
        string? StepNumber,
        string? StepName);
}
