using System.Text.Json;

using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

public record GetComplianceTemplateQuery(int Id) : IRequest<ComplianceFormTemplateResponseModel>;

public class GetComplianceTemplateHandler(
    AppDbContext db,
    IFormDefinitionBuilderFactory builderFactory,
    IPdfJsExtractorService pdfJsExtractor,
    IHttpClientFactory httpClientFactory,
    ILogger<GetComplianceTemplateHandler> logger)
    : IRequestHandler<GetComplianceTemplateQuery, ComplianceFormTemplateResponseModel>
{
    public async Task<ComplianceFormTemplateResponseModel> Handle(
        GetComplianceTemplateQuery request, CancellationToken ct)
    {
        var t = await db.ComplianceFormTemplates
            .Include(x => x.FormDefinitionVersions)
            .FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Compliance template {request.Id} not found.");

        var builder = builderFactory.TryGetBuilder(t.FormType);
        if (builder is not null)
        {
            var now = DateTimeOffset.UtcNow;
            var currentVersion = t.FormDefinitionVersions?
                .Where(v => v.IsActive && v.EffectiveDate <= now && (v.ExpirationDate == null || v.ExpirationDate > now))
                .OrderByDescending(v => v.EffectiveDate)
                .FirstOrDefault();

            var isStale = currentVersion is null
                || !StoredJsonHasBuilderVersion(currentVersion.FormDefinitionJson, builder.BuilderVersion);

            if (isStale)
            {
                logger.LogInformation(
                    "Builder version changed for template {TemplateId} ({FormType}) — auto-regenerating form definition",
                    t.Id, t.FormType);

                await RegenerateFromBuilderAsync(t, builder, currentVersion, now, ct);
            }
        }

        // Re-query after potential regeneration
        var refreshed = await db.ComplianceFormTemplates
            .AsNoTracking()
            .Include(x => x.FormDefinitionVersions)
            .FirstAsync(x => x.Id == request.Id, ct);

        return ComplianceTemplateMapper.ToResponse(refreshed);
    }

    private async Task RegenerateFromBuilderAsync(
        ComplianceFormTemplate template,
        IFormDefinitionBuilder builder,
        FormDefinitionVersion? staleVersion,
        DateTimeOffset now,
        CancellationToken ct)
    {
        try
        {
            PdfExtractionResult rawResult;

            if (!string.IsNullOrWhiteSpace(template.SourceUrl))
            {
                using var httpClient = httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (compatible; Forge/1.0)");
                var pdfBytes = await httpClient.GetByteArrayAsync(template.SourceUrl, ct);
                rawResult = await pdfJsExtractor.ExtractRawAsync(pdfBytes, ct);
            }
            else
            {
                rawResult = new PdfExtractionResult(0, []);
            }

            var json = builder.Build(rawResult);
            var fieldCount = System.Text.RegularExpressions.Regex.Matches(json, @"""id""").Count;

            // Expire the stale version
            if (staleVersion is not null)
            {
                var staleEntity = await db.FormDefinitionVersions.FindAsync([staleVersion.Id], ct);
                if (staleEntity is not null)
                    staleEntity.IsActive = false;
            }

            db.FormDefinitionVersions.Add(new FormDefinitionVersion
            {
                TemplateId = template.Id,
                FormDefinitionJson = json,
                SourceUrl = template.SourceUrl,
                EffectiveDate = now,
                Revision = now.ToString("yyyy-MM"),
                ExtractedAt = now,
                FieldCount = fieldCount,
                IsActive = true,
            });

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to auto-regenerate form definition for template {TemplateId}", template.Id);
        }
    }

    private static bool StoredJsonHasBuilderVersion(string json, string expectedVersion)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("builderVersion", out var bv))
                return bv.GetString() == expectedVersion;
        }
        catch { }
        return false;
    }
}
