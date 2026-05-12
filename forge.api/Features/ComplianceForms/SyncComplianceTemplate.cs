using System.Security.Cryptography;

using MediatR;

using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

public record SyncComplianceTemplateCommand(int Id) : IRequest;

public class SyncComplianceTemplateHandler(
    AppDbContext db,
    IDocumentSigningService signingService,
    IPdfJsExtractorService pdfJsExtractor,
    IFormDefinitionParser formParser,
    IFormDefinitionBuilderFactory builderFactory,
    IFormDefinitionVerifier formVerifier,
    IHttpClientFactory httpClientFactory,
    ILogger<SyncComplianceTemplateHandler> logger)
    : IRequestHandler<SyncComplianceTemplateCommand>
{
    public async Task Handle(SyncComplianceTemplateCommand request, CancellationToken ct)
    {
        var template = await db.ComplianceFormTemplates
            .FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Compliance template {request.Id} not found.");

        if (string.IsNullOrEmpty(template.SourceUrl))
            throw new InvalidOperationException($"Template {request.Id} has no SourceUrl configured.");

        using var httpClient = httpClientFactory.CreateClient();
        var pdfBytes = await httpClient.GetByteArrayAsync(template.SourceUrl, ct);

        var newHash = Convert.ToHexStringLower(SHA256.HashData(pdfBytes));

        // Check if the PDF has changed by comparing hash to the latest version
        var latestVersion = await db.FormDefinitionVersions
            .Where(v => v.TemplateId == template.Id && v.IsActive)
            .OrderByDescending(v => v.EffectiveDate)
            .FirstOrDefaultAsync(ct);

        // Check if the hardcoded builder version has changed (even without PDF change)
        var builder = builderFactory.TryGetBuilder(template.FormType);
        var builderVersionChanged = builder is not null && latestVersion is not null
            && !StoredJsonHasBuilderVersion(latestVersion.FormDefinitionJson, builder.BuilderVersion);

        if (newHash == latestVersion?.Sha256Hash && !builderVersionChanged)
        {
            template.LastSyncedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }

        var now = DateTimeOffset.UtcNow;

        // PDF changed — extract form definition via pdf.js and create new version
        try
        {
            var formType = template.FormType.ToString();

            // If a hardcoded builder exists, prefer it with a best-effort extraction (empty fallback if extraction fails)
            string json;
            PdfExtractionResult rawResult;
            if (builder is not null)
            {
                try { rawResult = await pdfJsExtractor.ExtractRawAsync(pdfBytes, ct); }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "pdf.js extraction failed for {FormType} — using hardcoded builder with empty extraction result", formType);
                    rawResult = new PdfExtractionResult(0, []);
                }
                logger.LogInformation("Using hardcoded builder {BuilderType} for {FormType}",
                    builder.GetType().Name, formType);
                json = builder.Build(rawResult);
            }
            else
            {
                rawResult = await pdfJsExtractor.ExtractRawAsync(pdfBytes, ct);
                json = formParser.Parse(rawResult, formType);
            }

            // Verify and refine with AI
            var verification = await formVerifier.VerifyAsync(json, rawResult, formType, ct);
            if (verification.CorrectedJson is not null)
                json = verification.CorrectedJson;

            var fieldCount = System.Text.RegularExpressions.Regex.Matches(json, @"""id""").Count;

            // Expire current version
            if (latestVersion is not null && latestVersion.ExpirationDate == null)
                latestVersion.ExpirationDate = now;

            // Create new version
            var version = new FormDefinitionVersion
            {
                TemplateId = template.Id,
                FormDefinitionJson = json,
                SourceUrl = template.SourceUrl,
                Sha256Hash = newHash,
                EffectiveDate = now,
                Revision = now.ToString("yyyy-MM"),
                ExtractedAt = now,
                FieldCount = fieldCount,
                IsActive = true,
            };
            db.FormDefinitionVersions.Add(version);

            logger.LogInformation("Created new form definition version for template {TemplateId} ({Name})",
                template.Id, template.Name);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not auto-extract form definition for template {TemplateId} — form will use download fallback",
                template.Id);
        }

        // Save form definition version first — DocuSeal is best-effort and must not block this
        template.Sha256Hash = newHash;
        template.LastSyncedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        // Upload to DocuSeal for e-signature (best-effort — failure does not block form definition)
        try
        {
            var docuSealTemplateId = await signingService.CreateTemplateFromPdfAsync(
                template.Name, pdfBytes, ct);

            if (template.DocuSealTemplateId.HasValue)
            {
                try { await signingService.DeleteTemplateAsync(template.DocuSealTemplateId.Value, ct); }
                catch { /* Old template cleanup is best-effort */ }
            }

            template.DocuSealTemplateId = docuSealTemplateId;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "DocuSeal upload failed for template {TemplateId} — e-signature unavailable but form definition is saved",
                template.Id);
        }
    }

    private static bool StoredJsonHasBuilderVersion(string json, string expectedVersion)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("builderVersion", out var bv))
                return bv.GetString() == expectedVersion;
        }
        catch { }
        return false;
    }
}
