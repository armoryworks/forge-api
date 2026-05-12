using System.Text.Json;

using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

/// <summary>
/// Compare the Angular-rendered form output against the source PDF rendering.
/// Runs structural (pixel-level) comparison and optional AI semantic comparison.
/// Stores the result on the FormDefinitionVersion.
/// </summary>
public record CompareFormRenderingCommand(int TemplateId) : IRequest<VisualComparisonResult>;

public class CompareFormRenderingHandler(
    AppDbContext db,
    IPdfJsExtractorService pdfJsExtractor,
    IFormRendererService formRenderer,
    IImageComparisonService imageComparison,
    IStorageService storageService,
    IHttpClientFactory httpClientFactory,
    IAiService aiService,
    ILogger<CompareFormRenderingHandler> logger)
    : IRequestHandler<CompareFormRenderingCommand, VisualComparisonResult>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public async Task<VisualComparisonResult> Handle(
        CompareFormRenderingCommand request, CancellationToken ct)
    {
        // Load the latest active FormDefinitionVersion for this template
        var version = await db.FormDefinitionVersions
            .Where(v => v.TemplateId == request.TemplateId && v.IsActive && v.ExpirationDate == null)
            .OrderByDescending(v => v.EffectiveDate)
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException(
                $"No active form definition version found for template {request.TemplateId}");

        var template = await db.ComplianceFormTemplates
            .FirstOrDefaultAsync(t => t.Id == request.TemplateId, ct)
            ?? throw new KeyNotFoundException($"Compliance template {request.TemplateId} not found");

        // Load PDF bytes (same logic as ExtractFormDefinition)
        var pdfBytes = await LoadPdfBytesAsync(template, ct);

        // 1. Render source PDF pages as PNGs
        logger.LogInformation("Rendering source PDF pages for template {TemplateId}", request.TemplateId);
        var sourcePngs = new List<byte[]>();
        var pageCount = (await pdfJsExtractor.ExtractRawAsync(pdfBytes, ct)).PageCount;
        for (var i = 1; i <= pageCount; i++)
        {
            var png = await pdfJsExtractor.RenderPageAsImageAsync(pdfBytes, i, 1.5, ct);
            sourcePngs.Add(png);
        }

        // 2. Render form definition pages as PNGs via Angular headless route
        logger.LogInformation("Rendering form definition pages via headless Angular");
        var renderedPngs = await formRenderer.RenderFormPagesAsync(version.FormDefinitionJson, ct);

        // 3. Structural comparison for each page pair
        var pageResults = new List<ImageComparisonResult>();
        var pairCount = Math.Min(sourcePngs.Count, renderedPngs.Count);

        for (var i = 0; i < pairCount; i++)
        {
            var result = await imageComparison.CompareAsync(sourcePngs[i], renderedPngs[i], ct);
            pageResults.Add(result);
            logger.LogInformation("Page {Page}: similarity={Similarity:F3}, passed={Passed}",
                i + 1, result.StructuralSimilarity, result.Passed);
        }

        // 4. AI semantic comparison (if available)
        bool? aiPassed = null;
        var aiIssues = new List<string>();

        try
        {
            if (pairCount > 0)
            {
                var prompt = "Compare these two images. The first is the source PDF form, the second is the rendered HTML form. " +
                             "List any significant visual differences in layout, fields, labels, or structure. " +
                             "Focus on missing fields, wrong labels, or layout problems. " +
                             "If they look substantially similar, say 'PASS'. If there are significant issues, say 'FAIL' and list them.";

                // Send first page pair for AI comparison (with 15s timeout — AI is optional)
                using var aiCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                aiCts.CancelAfter(TimeSpan.FromSeconds(15));

                var aiResult = await aiService.GenerateWithImageAsync(
                    prompt, sourcePngs[0],
                    "You are a form layout comparison expert. Be concise.",
                    aiCts.Token);

                if (!string.IsNullOrEmpty(aiResult))
                {
                    aiPassed = aiResult.Contains("PASS", StringComparison.OrdinalIgnoreCase)
                               && !aiResult.Contains("FAIL", StringComparison.OrdinalIgnoreCase);

                    if (aiPassed == false)
                    {
                        aiIssues.Add(aiResult);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException)
        {
            logger.LogDebug("AI semantic comparison unavailable — skipping ({Reason})", ex.GetType().Name);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AI semantic comparison failed — skipping");
        }

        // 5. Build aggregate result
        var structuralPassed = pageResults.All(r => r.Passed);
        var avgSimilarity = pageResults.Count > 0
            ? pageResults.Average(r => r.StructuralSimilarity)
            : 0;

        var comparison = new VisualComparisonResult(
            StructuralPassed: structuralPassed,
            StructuralSimilarity: avgSimilarity,
            PageResults: pageResults,
            AiSemanticPassed: aiPassed,
            AiIssues: aiIssues,
            ComparedAt: DateTimeOffset.UtcNow,
            SourcePageCount: sourcePngs.Count,
            RenderedPageCount: renderedPngs.Count);

        // 6. Store on the version
        version.VisualComparisonJson = JsonSerializer.Serialize(comparison, JsonOptions);
        version.VisualSimilarityScore = avgSimilarity;
        version.VisualComparisonPassed = structuralPassed && (aiPassed ?? true);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Visual comparison complete for template {TemplateId}: similarity={Similarity:F3}, passed={Passed}",
            request.TemplateId, avgSimilarity, comparison.StructuralPassed);

        return comparison;
    }

    private async Task<byte[]> LoadPdfBytesAsync(
        Forge.Core.Entities.ComplianceFormTemplate template, CancellationToken ct)
    {
        if (template.ManualOverrideFileId.HasValue)
        {
            var file = await db.FileAttachments
                .FirstOrDefaultAsync(f => f.Id == template.ManualOverrideFileId.Value, ct);
            if (file is not null)
            {
                using var stream = await storageService.DownloadAsync(file.BucketName, file.ObjectKey, ct);
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, ct);
                return ms.ToArray();
            }
        }

        if (!string.IsNullOrEmpty(template.SourceUrl))
        {
            using var httpClient = httpClientFactory.CreateClient();
            return await httpClient.GetByteArrayAsync(template.SourceUrl, ct);
        }

        throw new InvalidOperationException(
            $"Template '{template.Name}' has no SourceUrl or uploaded PDF.");
    }
}
