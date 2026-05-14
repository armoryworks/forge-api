using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

public record SaveFormDataCommand(
    int UserId,
    int TemplateId,
    string FormDataJson,
    int? FormDefinitionVersionId = null) : IRequest<ComplianceFormSubmissionResponseModel>;

public class SaveFormDataHandler(AppDbContext db)
    : IRequestHandler<SaveFormDataCommand, ComplianceFormSubmissionResponseModel>
{
    public async Task<ComplianceFormSubmissionResponseModel> Handle(
        SaveFormDataCommand request, CancellationToken ct)
    {
        var template = await db.ComplianceFormTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TemplateId, ct)
            ?? throw new KeyNotFoundException($"Compliance template {request.TemplateId} not found.");

        // Find or create submission for this user + template
        var submission = await db.ComplianceFormSubmissions
            .FirstOrDefaultAsync(s => s.UserId == request.UserId && s.TemplateId == request.TemplateId, ct);

        if (submission is null)
        {
            submission = new ComplianceFormSubmission
            {
                TemplateId = request.TemplateId,
                UserId = request.UserId,
                Status = ComplianceSubmissionStatus.Pending,
                FormDataJson = request.FormDataJson,
                FormDefinitionVersionId = request.FormDefinitionVersionId,
            };
            db.ComplianceFormSubmissions.Add(submission);
        }
        else
        {
            submission.FormDataJson = request.FormDataJson;
            // Pin to version on first save if not already set
            if (submission.FormDefinitionVersionId == null && request.FormDefinitionVersionId != null)
                submission.FormDefinitionVersionId = request.FormDefinitionVersionId;
        }

        await db.SaveChangesAsync(ct);

        return new ComplianceFormSubmissionResponseModel(
            submission.Id, submission.TemplateId, template.Name,
            template.FormType, submission.Status, submission.SignedAt,
            submission.SignedPdfFileId, submission.DocuSealSubmitUrl,
            submission.FormDataJson, submission.FormDefinitionVersionId,
            submission.CreatedAt,
            submission.FilledPdfFileId,
            submission.I9Section1SignedAt, submission.I9Section2SignedAt,
            submission.I9EmployerUserId, submission.I9DocumentListType,
            submission.I9DocumentDataJson, submission.I9Section2OverdueAt,
            submission.I9ReverificationDueAt
        );
    }
}
