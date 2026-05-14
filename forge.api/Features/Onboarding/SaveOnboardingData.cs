using System.Text.Json;

using MediatR;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

using EmployeeProfileEntity = Forge.Core.Entities.EmployeeProfile;

namespace Forge.Api.Features.Onboarding;

/// <summary>
/// Persists profile data, identity documents, and acknowledgments WITHOUT
/// touching DocuSeal or filling any PDFs.
///
/// This is step 1 of the per-form review flow:
///   save → [preview-pdf → sign-form] × N forms → done
///
/// Returns the list of compliance form types that need to be signed so the
/// frontend can drive the per-form review loop.
/// </summary>
public record SaveOnboardingDataCommand(
    int UserId,
    string UserEmail,
    string UserName,
    OnboardingSubmitRequestModel Model) : IRequest<SaveOnboardingResultModel>;

public class SaveOnboardingDataHandler(AppDbContext db, IConfiguration configuration)
    : IRequestHandler<SaveOnboardingDataCommand, SaveOnboardingResultModel>
{
    private bool IsMock => configuration.GetValue<bool>("MockIntegrations");

    public async Task<SaveOnboardingResultModel> Handle(
        SaveOnboardingDataCommand request, CancellationToken ct)
    {
        var m = request.Model;

        // ── 1. Persist profile ──────────────────────────────────────────────
        await UpsertProfileAsync(request.UserId, m, ct);

        // ── 2. Link identity documents ──────────────────────────────────────
        await SaveIdentityDocumentsAsync(request.UserId, m, ct);

        // ── 3. Mark acknowledgments ─────────────────────────────────────────
        if (m.AcknowledgeWorkersComp || m.AcknowledgeHandbook)
            await MarkAcknowledgmentsAsync(request.UserId, m.AcknowledgeWorkersComp, m.AcknowledgeHandbook, ct);

        // ── 4. Determine which forms need to be signed ──────────────────────
        var templates = await db.ComplianceFormTemplates
            .AsNoTracking()
            .Where(t => t.IsActive && new[]
            {
                ComplianceFormType.W4,
                ComplianceFormType.I9,
                ComplianceFormType.StateWithholding,
            }.Contains(t.FormType))
            .OrderBy(t => t.SortOrder)
            .ToListAsync(ct);

        // Only surface forms that can actually be signed. A template that lacks
        // AcroFieldMapJson + a blank PDF can't be filled, and SignOnboardingForm
        // would throw — bubbling up as a 500 mid-wizard (reported 2026-05-10).
        // In MockIntegrations mode the signing service returns synthetic URLs,
        // so unconfigured templates are still listed.
        var formsToSign = templates
            .Select(t => (
                Item: new OnboardingFormToSignItem(
                    t.FormType.ToString(),
                    t.Name,
                    HasTemplate: !string.IsNullOrWhiteSpace(t.AcroFieldMapJson) && t.FilledPdfTemplateId is not null),
                CanSign: IsMock || (!string.IsNullOrWhiteSpace(t.AcroFieldMapJson) && t.FilledPdfTemplateId is not null)))
            .Where(x => x.CanSign)
            .Select(x => x.Item)
            .ToList();

        return new SaveOnboardingResultModel(formsToSign);
    }

    private async Task UpsertProfileAsync(int userId, OnboardingSubmitRequestModel m, CancellationToken ct)
    {
        var profile = await db.EmployeeProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null)
        {
            profile = new EmployeeProfileEntity { UserId = userId };
            db.EmployeeProfiles.Add(profile);
        }

        profile.PhoneNumber = m.Phone;
        profile.PersonalEmail = m.Email;
        profile.DateOfBirth = m.DateOfBirth;
        profile.Street1 = m.Street1;
        profile.Street2 = m.Street2;
        profile.City = m.City;
        profile.State = m.AddressState;
        profile.ZipCode = m.ZipCode;
        profile.Country = "US";

        if (!string.IsNullOrWhiteSpace(m.BankName) && !string.IsNullOrWhiteSpace(m.RoutingNumber))
            profile.DirectDepositCompletedAt ??= DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is not null)
        {
            if (string.IsNullOrWhiteSpace(user.FirstName)) user.FirstName = m.FirstName;
            if (string.IsNullOrWhiteSpace(user.LastName)) user.LastName = m.LastName;
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task SaveIdentityDocumentsAsync(int userId, OnboardingSubmitRequestModel m, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(m.I9DocumentChoice)) return;

        var docs = new List<(IdentityDocumentType DocType, int? AttachmentId, DateTimeOffset? ExpiresAt)>();

        if (m.I9DocumentChoice == "A" && m.I9ListAFileAttachmentId.HasValue)
        {
            docs.Add((IdentityDocumentType.ListA, m.I9ListAFileAttachmentId, m.I9ListAExpiry));
        }
        else if (m.I9DocumentChoice == "BC")
        {
            if (m.I9ListBFileAttachmentId.HasValue)
                docs.Add((IdentityDocumentType.ListB, m.I9ListBFileAttachmentId, m.I9ListBExpiry));
            if (m.I9ListCFileAttachmentId.HasValue)
                docs.Add((IdentityDocumentType.ListC, m.I9ListCFileAttachmentId, m.I9ListCExpiry));
        }

        foreach (var (docType, attachmentId, expiresAt) in docs)
        {
            if (attachmentId is null) continue;

            // Skip if already recorded for this user+type
            var existing = await db.IdentityDocuments
                .FirstOrDefaultAsync(d => d.UserId == userId && d.DocumentType == docType, ct);
            if (existing is not null) continue;

            var identityDoc = new IdentityDocument
            {
                UserId = userId,
                DocumentType = docType,
                FileAttachmentId = attachmentId.Value,
                ExpiresAt = expiresAt.HasValue ? expiresAt.Value : null,
            };
            db.IdentityDocuments.Add(identityDoc);
            await db.SaveChangesAsync(ct);

            var attachment = await db.FileAttachments.FindAsync([attachmentId.Value], ct);
            if (attachment is not null)
            {
                attachment.EntityId = identityDoc.Id;
                await db.SaveChangesAsync(ct);
            }
        }
    }

    private async Task MarkAcknowledgmentsAsync(
        int userId, bool workersComp, bool handbook, CancellationToken ct)
    {
        var profile = await db.EmployeeProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? new EmployeeProfileEntity { UserId = userId };

        var now = DateTimeOffset.UtcNow;
        if (workersComp) profile.WorkersCompAcknowledgedAt = now;
        if (handbook)    profile.HandbookAcknowledgedAt    = now;

        await db.SaveChangesAsync(ct);
    }
}
