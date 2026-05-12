using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

public record UpdateComplianceTemplateCommand(
    int Id,
    UpdateComplianceFormTemplateRequestModel Model) : IRequest;

public class UpdateComplianceTemplateHandler(AppDbContext db)
    : IRequestHandler<UpdateComplianceTemplateCommand>
{
    public async Task Handle(UpdateComplianceTemplateCommand request, CancellationToken ct)
    {
        var template = await db.ComplianceFormTemplates
            .FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Compliance template {request.Id} not found.");

        var m = request.Model;

        template.Name = m.Name;
        template.FormType = m.FormType;
        template.Description = m.Description;
        template.Icon = m.Icon;
        template.SourceUrl = m.SourceUrl;
        template.IsAutoSync = m.IsAutoSync;
        template.IsActive = m.IsActive;
        template.SortOrder = m.SortOrder;
        template.RequiresIdentityDocs = m.RequiresIdentityDocs;
        template.BlocksJobAssignment = m.BlocksJobAssignment;
        template.ProfileCompletionKey = m.ProfileCompletionKey;
        template.DocuSealTemplateId = m.DocuSealTemplateId;
        template.AcroFieldMapJson = m.AcroFieldMapJson;

        await db.SaveChangesAsync(ct);
    }
}
