using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

public record GetMySubmissionByTypeQuery(
    int UserId,
    ComplianceFormType FormType) : IRequest<ComplianceFormSubmissionResponseModel?>;

public class GetMySubmissionByTypeHandler(AppDbContext db)
    : IRequestHandler<GetMySubmissionByTypeQuery, ComplianceFormSubmissionResponseModel?>
{
    public async Task<ComplianceFormSubmissionResponseModel?> Handle(
        GetMySubmissionByTypeQuery request, CancellationToken ct)
    {
        var submission = await db.ComplianceFormSubmissions
            .AsNoTracking()
            .Include(s => s.Template)
            .Where(s => s.UserId == request.UserId && s.Template.FormType == request.FormType)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (submission is null)
            return null;

        return new ComplianceFormSubmissionResponseModel(
            submission.Id, submission.TemplateId, submission.Template.Name,
            submission.Template.FormType, submission.Status, submission.SignedAt,
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
