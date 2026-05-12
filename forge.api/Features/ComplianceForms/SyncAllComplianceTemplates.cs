using System.Security.Cryptography;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

public record SyncAllComplianceTemplatesCommand : IRequest<int>;

public class SyncAllComplianceTemplatesHandler(
    AppDbContext db,
    IDocumentSigningService signingService,
    IHttpClientFactory httpClientFactory)
    : IRequestHandler<SyncAllComplianceTemplatesCommand, int>
{
    public async Task<int> Handle(SyncAllComplianceTemplatesCommand request, CancellationToken ct)
    {
        var templates = await db.ComplianceFormTemplates
            .Where(t => t.IsAutoSync && t.IsActive && t.SourceUrl != null)
            .ToListAsync(ct);

        var syncCount = 0;
        using var httpClient = httpClientFactory.CreateClient();

        foreach (var template in templates)
        {
            var pdfBytes = await httpClient.GetByteArrayAsync(template.SourceUrl!, ct);
            var newHash = Convert.ToHexStringLower(SHA256.HashData(pdfBytes));

            if (newHash == template.Sha256Hash)
            {
                template.LastSyncedAt = DateTimeOffset.UtcNow;
                syncCount++;
                continue;
            }

            var docuSealTemplateId = await signingService.CreateTemplateFromPdfAsync(
                template.Name, pdfBytes, ct);

            if (template.DocuSealTemplateId.HasValue)
            {
                try
                {
                    await signingService.DeleteTemplateAsync(template.DocuSealTemplateId.Value, ct);
                }
                catch
                {
                    // Old template cleanup is best-effort
                }
            }

            template.DocuSealTemplateId = docuSealTemplateId;
            template.Sha256Hash = newHash;
            template.LastSyncedAt = DateTimeOffset.UtcNow;
            syncCount++;
        }

        await db.SaveChangesAsync(ct);
        return syncCount;
    }
}
