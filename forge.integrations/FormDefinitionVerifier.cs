using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

/// <summary>
/// Verifies extracted ComplianceFormDefinition JSON against raw PDF data.
/// Runs structural checks and uses AI-assisted refinement when checks fail.
/// </summary>
public class FormDefinitionVerifier(
    IAiService aiService,
    IPdfJsExtractorService pdfJsExtractor,
    ILogger<FormDefinitionVerifier> logger) : IFormDefinitionVerifier
{
    private const int MaxRefinementIterations = 3;
    private const int MaxVisualIterations = 3;
    private const double FieldCoverageThreshold = 0.95;
    private const double TextCoverageThreshold = 0.80;

    public async Task<FormVerificationResult> VerifyAsync(
        string formDefinitionJson,
        PdfExtractionResult rawResult,
        string formType,
        CancellationToken ct)
    {
        logger.LogInformation("Verifying form definition for {FormType}", formType);

        var currentJson = formDefinitionJson;

        for (var iteration = 0; iteration <= MaxRefinementIterations; iteration++)
        {
            var result = RunStructuralChecks(currentJson, rawResult);

            if (result.Passed)
            {
                logger.LogInformation(
                    "Form definition verification passed (iteration {Iteration}, fields={FieldCov:P0}, text={TextCov:P0})",
                    iteration, result.FieldCoveragePercent, result.TextCoveragePercent);
                return result;
            }

            if (iteration == MaxRefinementIterations)
            {
                logger.LogWarning(
                    "Form definition verification failed after {MaxIterations} refinement iterations. " +
                    "Issues: {Issues}",
                    MaxRefinementIterations, string.Join("; ", result.Issues));
                return result;
            }

            // Attempt AI refinement
            logger.LogInformation(
                "Verification failed (iteration {Iteration}): {IssueCount} issues. Attempting AI refinement...",
                iteration, result.Issues.Count);

            try
            {
                var correctedJson = await RefineWithAiAsync(currentJson, rawResult, result, formType, ct);
                if (correctedJson is not null && IsValidJson(correctedJson))
                {
                    currentJson = correctedJson;
                    continue;
                }

                logger.LogWarning("AI refinement returned invalid JSON, stopping refinement");
                return result;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "AI refinement failed, returning current result");
                return result;
            }
        }

        // Should not reach here, but return a failed result as safety net
        return RunStructuralChecks(currentJson, rawResult);
    }

    public async Task<FormVerificationResult> VerifyWithVisualAsync(
        string formDefinitionJson,
        PdfExtractionResult rawResult,
        byte[] pdfBytes,
        string formType,
        CancellationToken ct)
    {
        // Phase 1: Run structural verification first (same as VerifyAsync)
        var structuralResult = await VerifyAsync(formDefinitionJson, rawResult, formType, ct);
        var currentJson = structuralResult.CorrectedJson ?? formDefinitionJson;

        // Phase 2: Visual verification — render source PDF pages and compare with AI vision
        logger.LogInformation("Starting visual verification for {FormType} ({PageCount} pages)",
            formType, rawResult.PageCount);

        var allVisualIssues = new List<string>();
        var visualPassed = true;

        try
        {
            var aiAvailable = await aiService.IsAvailableAsync(ct);
            if (!aiAvailable)
            {
                logger.LogWarning("AI service unavailable — skipping visual verification for {FormType}", formType);
                return structuralResult with
                {
                    VisualVerificationPassed = false,
                    VisualIssues = ["AI service unavailable — visual verification skipped"],
                };
            }

            for (var iteration = 0; iteration < MaxVisualIterations; iteration++)
            {
                var iterationIssues = new List<string>();

                for (var pageNum = 1; pageNum <= rawResult.PageCount; pageNum++)
                {
                    var pageImageBytes = await pdfJsExtractor.RenderPageAsImageAsync(pdfBytes, pageNum, 2.0, ct);

                    var pageIssues = await VerifyPageVisuallyAsync(
                        currentJson, pageImageBytes, pageNum, rawResult.PageCount, formType, ct);

                    if (pageIssues.Count > 0)
                        iterationIssues.AddRange(pageIssues.Select(i => $"Page {pageNum}: {i}"));
                }

                if (iterationIssues.Count == 0)
                {
                    logger.LogInformation(
                        "Visual verification passed for {FormType} (iteration {Iteration})",
                        formType, iteration);
                    visualPassed = true;
                    allVisualIssues.Clear();
                    break;
                }

                allVisualIssues = iterationIssues;
                logger.LogInformation(
                    "Visual verification found {IssueCount} issues (iteration {Iteration}/{Max}). Attempting AI correction...",
                    iterationIssues.Count, iteration, MaxVisualIterations);

                if (iteration < MaxVisualIterations - 1)
                {
                    var correctedJson = await RefineWithVisualFeedbackAsync(
                        currentJson, pdfBytes, iterationIssues, formType, ct);

                    if (correctedJson is not null && IsValidJson(correctedJson))
                    {
                        currentJson = correctedJson;
                        visualPassed = false;
                        continue;
                    }

                    logger.LogWarning("Visual AI refinement returned invalid JSON, stopping visual loop");
                    visualPassed = false;
                    break;
                }

                visualPassed = false;
            }
        }
        catch (NotSupportedException ex)
        {
            logger.LogWarning(ex, "Vision model not configured — skipping visual verification");
            return structuralResult with
            {
                VisualVerificationPassed = false,
                VisualIssues = [$"Vision model not configured: {ex.Message}"],
            };
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // HttpClient timeout — not a user cancellation
            logger.LogWarning(ex, "Visual verification timed out");
            allVisualIssues.Add($"Visual verification timed out: {ex.Message}");
            visualPassed = false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Visual verification failed with exception");
            allVisualIssues.Add($"Visual verification error: {ex.Message}");
            visualPassed = false;
        }

        var finalResult = structuralResult with
        {
            CorrectedJson = currentJson != formDefinitionJson ? currentJson : structuralResult.CorrectedJson,
            VisualVerificationPassed = visualPassed,
            VisualIssues = allVisualIssues,
        };

        if (visualPassed)
            logger.LogInformation("Visual verification completed successfully for {FormType}", formType);
        else
            logger.LogWarning("Visual verification completed with {IssueCount} unresolved issues for {FormType}",
                allVisualIssues.Count, formType);

        return finalResult;
    }

    /// <summary>
    /// Send a PDF page image + current form definition to AI vision model for layout comparison.
    /// Returns a list of layout issues found, or empty if the page matches.
    /// </summary>
    private async Task<List<string>> VerifyPageVisuallyAsync(
        string formDefinitionJson,
        byte[] pageImageBytes,
        int pageNumber,
        int totalPages,
        string formType,
        CancellationToken ct)
    {
        // Build a compact summary of the form definition for this page instead of sending full JSON
        var pageSummary = BuildPageSummary(formDefinitionJson, pageNumber);

        var systemPrompt = """
            You verify government form extractions. Compare the PDF image against the section/field summary.
            Reply with ONLY valid JSON, no other text. Format:
            {"layoutMatch":true,"issues":[]}
            or
            {"layoutMatch":false,"issues":["missing Step 2 section","field 'SSN' has wrong type"]}
            """;

        var prompt = $"""
            PDF page {pageNumber}/{totalPages} of {formType}. Does this summary match the image?

            {pageSummary}

            Reply JSON only:
            """;

        var response = await aiService.GenerateWithImageAsync(prompt, pageImageBytes, systemPrompt, ct);

        // Parse the AI response
        try
        {
            var jsonMatch = System.Text.RegularExpressions.Regex.Match(
                response, @"\{[\s\S]*\}", System.Text.RegularExpressions.RegexOptions.Singleline);

            if (!jsonMatch.Success)
            {
                logger.LogWarning("Visual verification AI response was not valid JSON: {Response}",
                    response.Length > 200 ? response[..200] : response);
                return [$"AI response not parseable for page {pageNumber}"];
            }

            var node = JsonNode.Parse(jsonMatch.Value);
            var layoutMatch = node?["layoutMatch"]?.GetValue<bool>() ?? false;

            if (layoutMatch) return [];

            var issues = node?["issues"]?.AsArray()
                ?.Select(i => i?.GetValue<string>() ?? "")
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .ToList() ?? [];

            return issues;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse visual verification AI response");
            return [$"Failed to parse AI response for page {pageNumber}: {ex.Message}"];
        }
    }

    /// <summary>
    /// Use AI vision to correct the form definition based on visual comparison issues.
    /// Sends the first page image for context along with the issues.
    /// </summary>
    private async Task<string?> RefineWithVisualFeedbackAsync(
        string currentJson,
        byte[] pdfBytes,
        List<string> visualIssues,
        string formType,
        CancellationToken ct)
    {
        // Render page 1 for context
        var pageImageBytes = await pdfJsExtractor.RenderPageAsImageAsync(pdfBytes, 1, 2.0, ct);

        var systemPrompt = """
            You are a government form definition correction assistant. You will receive:
            1. An image of the source PDF form
            2. The current ComplianceFormDefinition JSON
            3. A list of layout issues found during visual verification

            Your task is to correct the JSON to fix the layout issues while maintaining valid structure.
            Return ONLY the corrected ComplianceFormDefinition JSON, no explanation or markdown.
            """;

        var prompt = $"""
            Fix the following ComplianceFormDefinition JSON for a {formType} form based on these visual verification issues:

            ISSUES:
            {string.Join("\n", visualIssues.Select(i => $"  - {i}"))}

            CURRENT JSON:
            {currentJson}

            Return the corrected JSON only.
            """;

        var response = await aiService.GenerateWithImageAsync(prompt, pageImageBytes, systemPrompt, ct);

        var jsonMatch = System.Text.RegularExpressions.Regex.Match(
            response, @"\{[\s\S]*\}", System.Text.RegularExpressions.RegexOptions.Singleline);

        return jsonMatch.Success ? jsonMatch.Value : null;
    }

    /// <summary>
    /// Build a compact text summary of a specific page from the form definition JSON.
    /// Keeps the prompt small for the vision model.
    /// </summary>
    private static string BuildPageSummary(string formDefinitionJson, int pageNumber)
    {
        try
        {
            var root = JsonNode.Parse(formDefinitionJson);
            if (root is null) return "(empty definition)";

            var pages = root["pages"]?.AsArray();
            JsonNode? targetPage = null;

            if (pages is not null && pageNumber <= pages.Count)
                targetPage = pages[pageNumber - 1];

            if (targetPage is null)
            {
                // Flat sections — summarize all
                var flatSections = root["sections"]?.AsArray();
                if (flatSections is null) return "(no sections)";
                return SummarizeSections(flatSections);
            }

            var title = targetPage["title"]?.GetValue<string>() ?? $"Page {pageNumber}";
            var sections = targetPage["sections"]?.AsArray();
            if (sections is null) return $"{title}: (no sections)";

            return $"{title}:\n{SummarizeSections(sections)}";
        }
        catch
        {
            return "(could not parse definition)";
        }
    }

    private static string SummarizeSections(JsonArray sections)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var section in sections)
        {
            var sTitle = section?["title"]?.GetValue<string>() ?? "Untitled";
            var fields = section?["fields"]?.AsArray();
            var fieldCount = fields?.Count ?? 0;

            sb.AppendLine($"  Section: {sTitle} ({fieldCount} fields)");

            if (fields is null) continue;
            foreach (var field in fields)
            {
                var fId = field?["id"]?.GetValue<string>() ?? "?";
                var fType = field?["type"]?.GetValue<string>() ?? "text";
                var fLabel = field?["label"]?.GetValue<string>() ?? fId;

                // Skip HTML blobs in summary
                if (fType == "html") { sb.AppendLine("    - [HTML content block]"); continue; }

                // Truncate long labels
                if (fLabel.Length > 60) fLabel = fLabel[..60] + "...";
                sb.AppendLine($"    - {fLabel} ({fType})");
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Run structural verification checks against the raw extraction data.
    /// </summary>
    private FormVerificationResult RunStructuralChecks(string json, PdfExtractionResult rawResult)
    {
        var issues = new List<string>();
        var missingFieldIds = new List<string>();
        var orphanedFieldIds = new List<string>();

        // Parse the JSON
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            return new FormVerificationResult(
                false, 0, 0, [], [], [$"Invalid JSON: {ex.Message}"], null);
        }

        if (root is null)
        {
            return new FormVerificationResult(
                false, 0, 0, [], [], ["JSON parsed to null"], null);
        }

        // Extract all field IDs from the definition
        var definitionFieldIds = ExtractFieldIds(root).ToHashSet();

        // Extract all annotation IDs from raw data (excluding read-only and button types)
        var annotationIds = rawResult.Pages
            .SelectMany(p => p.Annotations)
            .Where(a => !a.ReadOnly && a.FieldType != "button")
            .Select(a => a.Id)
            .Distinct()
            .ToList();

        // For radio buttons, use the group name instead of individual button IDs
        var radioGroupNames = rawResult.Pages
            .SelectMany(p => p.Annotations)
            .Where(a => a.RadioGroupName is not null)
            .Select(a => a.RadioGroupName!)
            .Distinct()
            .ToHashSet();

        var effectiveAnnotationIds = annotationIds
            .Where(id => !radioGroupNames.Any(g =>
                rawResult.Pages.SelectMany(p => p.Annotations)
                    .Any(a => a.Id == id && a.RadioGroupName == g)))
            .Union(radioGroupNames)
            .ToHashSet();

        // Check 1: Field coverage — all annotations should have corresponding fields
        foreach (var annId in effectiveAnnotationIds)
        {
            if (!definitionFieldIds.Contains(annId))
                missingFieldIds.Add(annId);
        }

        var fieldCoverage = effectiveAnnotationIds.Count > 0
            ? 1.0 - (double)missingFieldIds.Count / effectiveAnnotationIds.Count
            : 1.0;

        if (missingFieldIds.Count > 0)
            issues.Add($"Missing {missingFieldIds.Count} field(s): {string.Join(", ", missingFieldIds.Take(10))}");

        // Check 2: Text coverage — bold/large text should appear as section titles
        var boldTextItems = rawResult.Pages
            .SelectMany(p => p.TextItems)
            .Where(t => t.IsBold && t.FontSize >= 11 && t.Text.Length > 3)
            .Select(t => t.Text.Trim())
            .Distinct()
            .ToList();

        var sectionTitles = ExtractSectionTitles(root).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matchedBoldText = boldTextItems.Count(bt =>
            sectionTitles.Any(st => st.Contains(bt, StringComparison.OrdinalIgnoreCase)
                                    || bt.Contains(st, StringComparison.OrdinalIgnoreCase)));

        var textCoverage = boldTextItems.Count > 0
            ? (double)matchedBoldText / boldTextItems.Count
            : 1.0;

        if (textCoverage < TextCoverageThreshold)
            issues.Add($"Text coverage {textCoverage:P0} below threshold {TextCoverageThreshold:P0}");

        // Check 3: No orphaned fields — every field should be in a section
        var fieldsInSections = ExtractFieldIdsInSections(root).ToHashSet();
        foreach (var fieldId in definitionFieldIds)
        {
            if (!fieldsInSections.Contains(fieldId))
                orphanedFieldIds.Add(fieldId);
        }

        if (orphanedFieldIds.Count > 0)
            issues.Add($"Orphaned fields not in any section: {string.Join(", ", orphanedFieldIds.Take(10))}");

        // Check 4: Structural validity — has pages with sections with fields
        var pages = root["pages"]?.AsArray();
        if (pages is null || pages.Count == 0)
        {
            var sections = root["sections"]?.AsArray();
            if (sections is null || sections.Count == 0)
                issues.Add("No pages or sections found in definition");
        }

        var passed = fieldCoverage >= FieldCoverageThreshold
                     && textCoverage >= TextCoverageThreshold
                     && orphanedFieldIds.Count == 0
                     && issues.Count == 0;

        return new FormVerificationResult(
            passed, fieldCoverage, textCoverage,
            missingFieldIds, orphanedFieldIds, issues, null);
    }

    /// <summary>
    /// Use AI to correct the form definition based on verification failures.
    /// </summary>
    private async Task<string?> RefineWithAiAsync(
        string currentJson,
        PdfExtractionResult rawResult,
        FormVerificationResult verificationResult,
        string formType,
        CancellationToken ct)
    {
        var annotationSummary = string.Join("\n", rawResult.Pages.SelectMany(p => p.Annotations)
            .Select(a => $"  - {a.Id} ({a.FieldType}): {a.AlternativeText ?? a.FieldName ?? "no label"}"));

        var prompt = $"""
            You are a form definition correction assistant. Fix the following ComplianceFormDefinition JSON.

            FORM TYPE: {formType}

            CURRENT JSON (with issues):
            {currentJson}

            VERIFICATION ISSUES:
            {string.Join("\n", verificationResult.Issues.Select(i => $"  - {i}"))}

            MISSING FIELD IDS (must be added to the definition):
            {string.Join("\n", verificationResult.MissingFieldIds.Select(id => $"  - {id}"))}

            ORPHANED FIELD IDS (must be placed in appropriate sections):
            {string.Join("\n", verificationResult.OrphanedFieldIds.Select(id => $"  - {id}"))}

            PDF ANNOTATIONS (ground truth — all of these must appear in the definition):
            {annotationSummary}

            RULES:
            1. Every annotation ID must appear as a field in the definition
            2. Every field must be inside a section
            3. Preserve existing section structure and layout metadata
            4. Add missing fields to the most appropriate section based on their label
            5. Return ONLY the corrected JSON, no explanation

            Return the corrected ComplianceFormDefinition JSON:
            """;

        var response = await aiService.GenerateTextAsync(prompt, ct);

        // Extract JSON from the response (AI may wrap it in markdown code blocks)
        var jsonMatch = System.Text.RegularExpressions.Regex.Match(
            response, @"\{[\s\S]*\}", System.Text.RegularExpressions.RegexOptions.Singleline);

        return jsonMatch.Success ? jsonMatch.Value : null;
    }

    // ─── JSON Traversal Helpers ─────────────────────────────────────────────────

    private static IEnumerable<string> ExtractFieldIds(JsonNode root)
    {
        var pages = root["pages"]?.AsArray();
        if (pages is not null)
        {
            foreach (var page in pages)
            {
                var sections = page?["sections"]?.AsArray();
                if (sections is null) continue;
                foreach (var id in ExtractFieldIdsFromSections(sections))
                    yield return id;
            }
        }

        var flatSections = root["sections"]?.AsArray();
        if (flatSections is not null)
        {
            foreach (var id in ExtractFieldIdsFromSections(flatSections))
                yield return id;
        }
    }

    private static IEnumerable<string> ExtractFieldIdsFromSections(JsonArray sections)
    {
        foreach (var section in sections)
        {
            var fields = section?["fields"]?.AsArray();
            if (fields is null) continue;
            foreach (var field in fields)
            {
                var id = field?["id"]?.GetValue<string>();
                if (id is not null)
                    yield return id;
            }
        }
    }

    private static IEnumerable<string> ExtractFieldIdsInSections(JsonNode root) =>
        ExtractFieldIds(root); // Same traversal — all fields are in sections by definition

    private static IEnumerable<string> ExtractSectionTitles(JsonNode root)
    {
        var pages = root["pages"]?.AsArray();
        if (pages is not null)
        {
            foreach (var page in pages)
            {
                var sections = page?["sections"]?.AsArray();
                if (sections is null) continue;
                foreach (var section in sections)
                {
                    var title = section?["title"]?.GetValue<string>();
                    if (title is not null)
                        yield return title;
                }
            }
        }

        var flatSections = root["sections"]?.AsArray();
        if (flatSections is not null)
        {
            foreach (var section in flatSections)
            {
                var title = section?["title"]?.GetValue<string>();
                if (title is not null)
                    yield return title;
            }
        }
    }

    private static bool IsValidJson(string json)
    {
        try
        {
            JsonNode.Parse(json);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
