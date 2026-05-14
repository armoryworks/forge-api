using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

public record CreateSubmissionCommand(
    int UserId,
    int TemplateId,
    string Email,
    string Name) : IRequest<ComplianceFormSubmissionResponseModel>;

public class CreateSubmissionHandler(
    AppDbContext db,
    IDocumentSigningService signingService)
    : IRequestHandler<CreateSubmissionCommand, ComplianceFormSubmissionResponseModel>
{
    public async Task<ComplianceFormSubmissionResponseModel> Handle(
        CreateSubmissionCommand request, CancellationToken ct)
    {
        var template = await db.ComplianceFormTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TemplateId, ct)
            ?? throw new KeyNotFoundException($"Compliance template {request.TemplateId} not found.");

        if (!template.DocuSealTemplateId.HasValue)
            throw new InvalidOperationException(
                $"Template {request.TemplateId} has no DocuSeal template configured. Sync the template first.");

        var result = await signingService.CreateSubmissionAsync(
            template.DocuSealTemplateId.Value, request.Email, request.Name, ct);

        var submission = new ComplianceFormSubmission
        {
            TemplateId = request.TemplateId,
            UserId = request.UserId,
            DocuSealSubmissionId = result.SubmissionId,
            Status = ComplianceSubmissionStatus.Pending,
            DocuSealSubmitUrl = result.SubmitUrl,
        };

        db.ComplianceFormSubmissions.Add(submission);
        await db.SaveChangesAsync(ct);

        return new ComplianceFormSubmissionResponseModel(
            submission.Id, submission.TemplateId, template.Name,
            template.FormType, submission.Status, submission.SignedAt,
            submission.SignedPdfFileId, submission.DocuSealSubmitUrl,
            submission.FormDataJson, submission.FormDefinitionVersionId, submission.CreatedAt,
            submission.FilledPdfFileId,
            submission.I9Section1SignedAt, submission.I9Section2SignedAt,
            submission.I9EmployerUserId, submission.I9DocumentListType,
            submission.I9DocumentDataJson, submission.I9Section2OverdueAt,
            submission.I9ReverificationDueAt
        );
    }
}
