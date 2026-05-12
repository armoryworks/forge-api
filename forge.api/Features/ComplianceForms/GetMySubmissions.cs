using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

public record GetMySubmissionsQuery(int UserId) : IRequest<List<ComplianceFormSubmissionResponseModel>>;

public class GetMySubmissionsHandler(AppDbContext db)
    : IRequestHandler<GetMySubmissionsQuery, List<ComplianceFormSubmissionResponseModel>>
{
    public async Task<List<ComplianceFormSubmissionResponseModel>> Handle(
        GetMySubmissionsQuery request, CancellationToken ct)
    {
        var submissions = await db.ComplianceFormSubmissions
            .AsNoTracking()
            .Include(s => s.Template)
            .Where(s => s.UserId == request.UserId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

        return submissions.Select(s => new ComplianceFormSubmissionResponseModel(
            s.Id, s.TemplateId, s.Template.Name, s.Template.FormType,
            s.Status, s.SignedAt, s.SignedPdfFileId, s.DocuSealSubmitUrl,
            s.FormDataJson, s.FormDefinitionVersionId, s.CreatedAt,
            s.FilledPdfFileId,
            s.I9Section1SignedAt, s.I9Section2SignedAt,
            s.I9EmployerUserId, s.I9DocumentListType,
            s.I9DocumentDataJson, s.I9Section2OverdueAt,
            s.I9ReverificationDueAt
        )).ToList();
    }
}
