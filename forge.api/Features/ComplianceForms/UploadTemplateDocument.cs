using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

public record UploadTemplateDocumentCommand(int TemplateId, int FileAttachmentId) : IRequest<ComplianceFormTemplateResponseModel>;

public class UploadTemplateDocumentHandler(AppDbContext db, IMediator mediator)
    : IRequestHandler<UploadTemplateDocumentCommand, ComplianceFormTemplateResponseModel>
{
    public async Task<ComplianceFormTemplateResponseModel> Handle(
        UploadTemplateDocumentCommand request, CancellationToken ct)
    {
        var template = await db.ComplianceFormTemplates
            .Include(x => x.FormDefinitionVersions)
            .FirstOrDefaultAsync(x => x.Id == request.TemplateId, ct)
            ?? throw new KeyNotFoundException($"Compliance template {request.TemplateId} not found.");

        var file = await db.FileAttachments
            .FirstOrDefaultAsync(x => x.Id == request.FileAttachmentId, ct)
            ?? throw new KeyNotFoundException($"File attachment {request.FileAttachmentId} not found.");

        template.ManualOverrideFileId = request.FileAttachmentId;
        await db.SaveChangesAsync(ct);

        // Auto-extract form definition from the uploaded PDF
        if (file.ContentType == "application/pdf")
        {
            try
            {
                await mediator.Send(new ExtractFormDefinitionCommand(template.Id), ct);

                // Reload template with new version
                template = await db.ComplianceFormTemplates
                    .Include(x => x.FormDefinitionVersions)
                    .FirstAsync(x => x.Id == request.TemplateId, ct);
            }
            catch
            {
                // Extraction failure is non-fatal — template still usable with PDF download fallback
            }
        }

        return ComplianceTemplateMapper.ToResponse(template);
    }
}
