using System.Security.Cryptography;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

public class ComplianceFormSyncJob(
    AppDbContext db,
    IDocumentSigningService signingService,
    IHttpClientFactory httpClientFactory,
    IClock clock,
    ILogger<ComplianceFormSyncJob> logger)
{
    public async Task SyncFederalFormsAsync(CancellationToken ct = default)
    {
        var available = await signingService.IsAvailableAsync(ct);
        if (!available)
        {
            logger.LogInformation("Document signing service unavailable — skipping compliance form sync");
            return;
        }

        var templates = await db.ComplianceFormTemplates
            .Where(t => t.IsAutoSync && t.SourceUrl != null && t.ManualOverrideFileId == null && t.IsActive)
            .Where(t => t.DeletedAt == null)
            .ToListAsync(ct);

        if (templates.Count == 0)
        {
            logger.LogInformation("No auto-sync compliance templates found");
            return;
        }

        var httpClient = httpClientFactory.CreateClient();
        var synced = 0;

        foreach (var template in templates)
        {
            try
            {
                var pdfBytes = await httpClient.GetByteArrayAsync(template.SourceUrl, ct);
                var hash = Convert.ToHexStringLower(SHA256.HashData(pdfBytes));

                if (hash == template.Sha256Hash)
                {
                    logger.LogDebug("Template {Name} unchanged (hash match)", template.Name);
                    continue;
                }

                var docuSealTemplateId = await signingService.CreateTemplateFromPdfAsync(
                    template.Name, pdfBytes, ct);

                // Delete old DocuSeal template if one existed
                if (template.DocuSealTemplateId.HasValue)
                {
                    try
                    {
                        await signingService.DeleteTemplateAsync(
                            template.DocuSealTemplateId.Value, ct);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to delete old DocuSeal template {Id}", template.DocuSealTemplateId);
                    }
                }

                template.DocuSealTemplateId = docuSealTemplateId;
                template.Sha256Hash = hash;
                template.LastSyncedAt = clock.UtcNow;
                synced++;

                logger.LogInformation("Synced template {Name} → DocuSeal template {Id}", template.Name, docuSealTemplateId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to sync template {Name} from {Url}", template.Name, template.SourceUrl);
            }
        }

        if (synced > 0)
            await db.SaveChangesAsync(ct);

        logger.LogInformation("Compliance form sync complete: {Synced}/{Total} templates updated", synced, templates.Count);
    }
}
