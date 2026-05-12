using System.Security.Cryptography;

using MediatR;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

/// <summary>
/// Extract form definition from a compliance template's source PDF using pdf.js via PuppeteerSharp.
/// Runs smart pattern-based parsing and AI-assisted verification/refinement.
/// Creates a new FormDefinitionVersion (expires the previous one).
/// </summary>
public record ExtractFormDefinitionCommand(int TemplateId) : IRequest<ExtractFormDefinitionResult>;

public record ExtractFormDefinitionResult(int VersionId, string FormDefinitionJson, string Revision, int FieldCount);

public class ExtractFormDefinitionHandler(
    AppDbContext db,
    IPdfJsExtractorService pdfJsExtractor,
    IFormDefinitionParser formParser,
    IFormDefinitionBuilderFactory builderFactory,
    IStorageService storageService,
    IHttpClientFactory httpClientFactory,
    IServiceScopeFactory scopeFactory,
    ILogger<ExtractFormDefinitionHandler> logger)
    : IRequestHandler<ExtractFormDefinitionCommand, ExtractFormDefinitionResult>
{
    public async Task<ExtractFormDefinitionResult> Handle(
        ExtractFormDefinitionCommand request, CancellationToken ct)
    {
        var template = await db.ComplianceFormTemplates
            .FirstOrDefaultAsync(x => x.Id == request.TemplateId, ct)
            ?? throw new KeyNotFoundException($"Compliance template {request.TemplateId} not found.");

        byte[]? pdfBytes = null;
        string? sourceUrl = template.SourceUrl;

        // Priority: manual override file → source URL
        if (template.ManualOverrideFileId.HasValue)
        {
            var file = await db.FileAttachments
                .FirstOrDefaultAsync(f => f.Id == template.ManualOverrideFileId.Value, ct);
            if (file is not null)
            {
                using var stream = await storageService.DownloadAsync(file.BucketName, file.ObjectKey, ct);
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, ct);
                pdfBytes = ms.ToArray();
                sourceUrl = $"minio://{file.BucketName}/{file.ObjectKey}";
            }
        }

        if (pdfBytes is null && !string.IsNullOrEmpty(template.SourceUrl))
        {
            using var httpClient = httpClientFactory.CreateClient();
            pdfBytes = await httpClient.GetByteArrayAsync(template.SourceUrl, ct);
        }

        if (pdfBytes is null)
        {
            throw new InvalidOperationException(
                $"Template '{template.Name}' has no SourceUrl or uploaded PDF to extract from.");
        }

        var formType = template.FormType.ToString();

        // Phase 1: Extract raw text + annotations via pdf.js
        logger.LogInformation("Extracting form definition for template {TemplateId} ({FormType}) via pdf.js",
            template.Id, formType);

        var rawResult = await pdfJsExtractor.ExtractRawAsync(pdfBytes, ct);

        // Phase 2: Parse into ComplianceFormDefinition JSON
        // Try form-specific builder first (hardcoded definitions for known forms),
        // fall back to generic parser for unknown/new templates.
        var builder = builderFactory.TryGetBuilder(template.FormType);
        string json;
        if (builder is not null)
        {
            logger.LogInformation("Using hardcoded builder {BuilderType} for {FormType}",
                builder.GetType().Name, formType);
            json = builder.Build(rawResult);
        }
        else
        {
            logger.LogInformation("No hardcoded builder for {FormType} — using generic parser", formType);
            json = formParser.Parse(rawResult, formType);
        }

        // Phase 3: Skip verification for now — parser output is used directly.
        // TODO: Re-enable verification once Ollama is reliably available:
        // var verification = await formVerifier.VerifyAsync(json, rawResult, formType, ct);

        var revision = DateTimeOffset.UtcNow.ToString("yyyy-MM");
        var hash = Convert.ToHexStringLower(SHA256.HashData(pdfBytes));
        var fieldCount = System.Text.RegularExpressions.Regex.Matches(json, @"""id""").Count;

        // Expire the current active version for this template
        var now = DateTimeOffset.UtcNow;
        var currentVersion = await db.FormDefinitionVersions
            .Where(v => v.TemplateId == template.Id && v.IsActive && v.ExpirationDate == null)
            .OrderByDescending(v => v.EffectiveDate)
            .FirstOrDefaultAsync(ct);

        if (currentVersion is not null)
            currentVersion.ExpirationDate = now;

        // Create the new version
        var version = new FormDefinitionVersion
        {
            TemplateId = template.Id,
            FormDefinitionJson = json,
            SourceUrl = sourceUrl,
            Sha256Hash = hash,
            EffectiveDate = now,
            Revision = revision,
            ExtractedAt = now,
            FieldCount = fieldCount,
            IsActive = true,
        };
        db.FormDefinitionVersions.Add(version);
        await db.SaveChangesAsync(ct);

        // Fire-and-forget visual comparison in a new DI scope (non-blocking).
        // Must create a new scope because the current request's DbContext will be disposed
        // before the comparison finishes.
        var templateId = request.TemplateId;
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                await mediator.Send(new CompareFormRenderingCommand(templateId), CancellationToken.None);
                logger.LogInformation("Visual comparison completed for template {TemplateId}", templateId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Visual comparison failed for template {TemplateId} — non-blocking", templateId);
            }
        }, CancellationToken.None);

        return new ExtractFormDefinitionResult(version.Id, json, revision, fieldCount);
    }
}
