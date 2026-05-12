using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

public record GetUserComplianceDetailQuery(int UserId) : IRequest<UserComplianceDetailResponseModel>;

public class GetUserComplianceDetailHandler(AppDbContext db)
    : IRequestHandler<GetUserComplianceDetailQuery, UserComplianceDetailResponseModel>
{
    public async Task<UserComplianceDetailResponseModel> Handle(
        GetUserComplianceDetailQuery request, CancellationToken ct)
    {
        var user = await db.Users
            .AsNoTracking()
            .Include(u => u.WorkLocation)
            .FirstOrDefaultAsync(u => u.Id == request.UserId, ct)
            ?? throw new KeyNotFoundException($"User {request.UserId} not found.");

        var submissions = await db.ComplianceFormSubmissions
            .AsNoTracking()
            .Include(s => s.Template)
            .Where(s => s.UserId == request.UserId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

        var identityDocs = await db.IdentityDocuments
            .AsNoTracking()
            .Include(d => d.FileAttachment)
            .Where(d => d.UserId == request.UserId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

        var verifierIds = identityDocs
            .Where(d => d.VerifiedById.HasValue)
            .Select(d => d.VerifiedById!.Value)
            .Distinct()
            .ToList();

        var verifiers = verifierIds.Count > 0
            ? await db.Users.AsNoTracking()
                .Where(u => verifierIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}", ct)
            : new Dictionary<int, string>();

        var submissionModels = submissions.Select(s => new ComplianceFormSubmissionResponseModel(
            s.Id, s.TemplateId, s.Template.Name, s.Template.FormType,
            s.Status, s.SignedAt, s.SignedPdfFileId, s.DocuSealSubmitUrl,
            s.FormDataJson, s.FormDefinitionVersionId, s.CreatedAt,
            s.FilledPdfFileId,
            s.I9Section1SignedAt, s.I9Section2SignedAt,
            s.I9EmployerUserId, s.I9DocumentListType,
            s.I9DocumentDataJson, s.I9Section2OverdueAt,
            s.I9ReverificationDueAt
        )).ToList();

        var identityDocModels = identityDocs.Select(d => new IdentityDocumentResponseModel(
            d.Id, d.UserId, d.DocumentType, d.FileAttachmentId,
            d.FileAttachment.FileName, d.VerifiedAt, d.VerifiedById,
            d.VerifiedById.HasValue && verifiers.TryGetValue(d.VerifiedById.Value, out var name) ? name : null,
            d.ExpiresAt, d.Notes, d.CreatedAt
        )).ToList();

        // Resolve per-employee state withholding info
        var stateInfo = await ResolveStateWithholdingInfoAsync(user, ct);

        return new UserComplianceDetailResponseModel(
            user.Id,
            $"{user.LastName}, {user.FirstName}",
            user.Email ?? string.Empty,
            submissionModels,
            identityDocModels,
            stateInfo
        );
    }

    private async Task<StateWithholdingInfoModel?> ResolveStateWithholdingInfoAsync(
        ApplicationUser user, CancellationToken ct)
    {
        // 1. User's work location state
        var stateCode = user.WorkLocation?.State;
        var source = "Work Location";

        // 2. Default company location
        if (string.IsNullOrWhiteSpace(stateCode))
        {
            var defaultLocation = await db.CompanyLocations
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.IsDefault && l.IsActive, ct);
            stateCode = defaultLocation?.State;
            source = "Default Location";
        }

        // 3. company_state system setting
        if (string.IsNullOrWhiteSpace(stateCode))
        {
            var setting = await db.SystemSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "company_state", ct);
            stateCode = setting?.Value;
            source = "Company Setting";
        }

        if (string.IsNullOrWhiteSpace(stateCode))
            return null;

        var stateRef = await db.ReferenceData
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.GroupCode == "state_withholding" && r.Code == stateCode, ct);

        if (stateRef is null)
            return null;

        var category = "state_form";
        string? formName = null;

        if (!string.IsNullOrWhiteSpace(stateRef.Metadata))
        {
            try
            {
                using var doc = JsonDocument.Parse(stateRef.Metadata);
                if (doc.RootElement.TryGetProperty("category", out var cat))
                    category = cat.GetString() ?? "state_form";
                if (doc.RootElement.TryGetProperty("formName", out var form))
                    formName = form.GetString();
            }
            catch (JsonException) { }
        }

        return new StateWithholdingInfoModel(stateCode, stateRef.Label, category, formName, source);
    }
}
