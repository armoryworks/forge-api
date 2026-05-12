using MediatR;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

public record CreateComplianceTemplateCommand(
    CreateComplianceFormTemplateRequestModel Model) : IRequest<ComplianceFormTemplateResponseModel>;

public class CreateComplianceTemplateHandler(AppDbContext db)
    : IRequestHandler<CreateComplianceTemplateCommand, ComplianceFormTemplateResponseModel>
{
    public async Task<ComplianceFormTemplateResponseModel> Handle(
        CreateComplianceTemplateCommand request, CancellationToken ct)
    {
        var m = request.Model;

        var template = new ComplianceFormTemplate
        {
            Name = m.Name,
            FormType = m.FormType,
            Description = m.Description,
            Icon = m.Icon,
            SourceUrl = m.SourceUrl,
            IsAutoSync = m.IsAutoSync,
            IsActive = m.IsActive,
            SortOrder = m.SortOrder,
            RequiresIdentityDocs = m.RequiresIdentityDocs,
            BlocksJobAssignment = m.BlocksJobAssignment,
            ProfileCompletionKey = m.ProfileCompletionKey,
            FormDefinitionVersions = [],
        };

        db.ComplianceFormTemplates.Add(template);
        await db.SaveChangesAsync(ct);

        return ComplianceTemplateMapper.ToResponse(template);
    }
}
