using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

public record SetBlankPdfTemplateCommand(int TemplateId, int FileAttachmentId) : IRequest<ComplianceFormTemplateResponseModel>;

public class SetBlankPdfTemplateHandler(AppDbContext db)
    : IRequestHandler<SetBlankPdfTemplateCommand, ComplianceFormTemplateResponseModel>
{
    public async Task<ComplianceFormTemplateResponseModel> Handle(
        SetBlankPdfTemplateCommand request, CancellationToken ct)
    {
        var template = await db.ComplianceFormTemplates
            .Include(x => x.FormDefinitionVersions)
            .FirstOrDefaultAsync(x => x.Id == request.TemplateId, ct)
            ?? throw new KeyNotFoundException($"Compliance template {request.TemplateId} not found.");

        _ = await db.FileAttachments
            .FirstOrDefaultAsync(x => x.Id == request.FileAttachmentId, ct)
            ?? throw new KeyNotFoundException($"File attachment {request.FileAttachmentId} not found.");

        template.FilledPdfTemplateId = request.FileAttachmentId;
        await db.SaveChangesAsync(ct);

        return ComplianceTemplateMapper.ToResponse(template);
    }
}
