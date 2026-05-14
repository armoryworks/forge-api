using System.Text.Json;

using MediatR;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Onboarding;

/// <summary>
/// Processes the unified onboarding wizard submission.
///
/// For each configured government form (W-4, I-9, state withholding):
///   1. Serializes all fields into a flat JSON object using canonical logical key names
///   2. Delegates to FillAndSubmitFormForSigningCommand which maps keys via AcroFieldMapJson
///   3. Returns DocuSeal embed URLs for the signing step
///
/// Also persists profile data (address, direct deposit) and marks acknowledgments complete.
/// </summary>
public record SubmitOnboardingCommand(
    int UserId,
    string UserEmail,
    string UserName,
    OnboardingSubmitRequestModel Model) : IRequest<OnboardingSubmitResultModel>;

public class SubmitOnboardingHandler(
    AppDbContext db,
    IMediator mediator,
    IDocumentSigningService signingService,
    IConfiguration configuration,
    IPiiProtector pii)
    : IRequestHandler<SubmitOnboardingCommand, OnboardingSubmitResultModel>
{
    private bool IsMock => configuration.GetValue<bool>("MockIntegrations");
    public async Task<OnboardingSubmitResultModel> Handle(
        SubmitOnboardingCommand request, CancellationToken ct)
    {
        var m = request.Model;

        // ── 1. Build flat JSON with ALL logical field names ──────────────────
        // This JSON is passed to FillAndSubmitFormForSigningCommand.
        // Each template's AcroFieldMapJson maps a subset of these keys to AcroForm field names.
        var formData = BuildFormDataDictionary(m);
        // Backfill SSN / bank routing / bank account from the encrypted draft
        // when the wizard sent them blank (the user already supplied them in
        // an earlier step + the "Securely stored" indicator was shown).
        await OnboardingPiiMerge.MergeStoredPiiAsync(db, pii, request.UserId, formData, ct);
        var formDataJson = JsonSerializer.Serialize(formData);

        // ── 2. Load templates for forms that need PDF fill + signing ─────────
        var templates = await db.ComplianceFormTemplates
            .AsNoTracking()
            .Where(t => t.IsActive && new[]
            {
                ComplianceFormType.W4,
                ComplianceFormType.I9,
                ComplianceFormType.StateWithholding
            }.Contains(t.FormType))
            .ToListAsync(ct);

        var signingUrls = new List<OnboardingSigningUrlModel>();
        int? i9EmployerSubmitterId = null;

        // Fallback signing is available when running in mock mode OR when DocuSeal is configured
        // but the template hasn't been set up for AcroForm pre-fill yet.
        var canFallbackSign = IsMock || await signingService.IsAvailableAsync(ct);

        // ── 3. Fill + submit each form to DocuSeal ───────────────────────────
        foreach (var template in templates.OrderBy(t => t.SortOrder))
        {
            ComplianceForms.FillAndSubmitResult result;

            if (string.IsNullOrWhiteSpace(template.AcroFieldMapJson) || template.FilledPdfTemplateId is null)
            {
                // Template not yet configured for PDF fill+sign.
                // Use fallback path (mock or DocuSeal without pre-fill) if available,
                // otherwise skip this template entirely.
                if (!canFallbackSign) continue;

                var isI9Template = template.FormType == ComplianceFormType.I9;
                IReadOnlyList<SequentialSubmitter> submitters = isI9Template
                    ? [
                        new SequentialSubmitter(1, request.UserEmail, request.UserName, "Employee"),
                        new SequentialSubmitter(2, "employer@placeholder.local", "Employer", "Employer"),
                      ]
                    : [new SequentialSubmitter(1, request.UserEmail, request.UserName, "Employee")];

                var templateName = $"[MOCK] {template.Name} — {request.UserName} — {DateTimeOffset.UtcNow:yyyyMMdd}";
                var multiSub = await signingService.CreateSubmissionFromPdfAsync(
                    templateName, [], submitters, ct);

                if (!multiSub.SubmittersByOrder.TryGetValue(1, out var s1)) continue;
                var s2 = isI9Template && multiSub.SubmittersByOrder.TryGetValue(2, out var s2r) ? s2r : null;

                // Upsert a lightweight submission record so the admin panel shows it
                var mockSubmission = await db.ComplianceFormSubmissions
                    .FirstOrDefaultAsync(s => s.UserId == request.UserId && s.TemplateId == template.Id, ct);
                if (mockSubmission is null)
                {
                    mockSubmission = new ComplianceFormSubmission
                    {
                        TemplateId = template.Id,
                        UserId = request.UserId,
                        Status = ComplianceSubmissionStatus.Pending,
                    };
                    db.ComplianceFormSubmissions.Add(mockSubmission);
                }
                mockSubmission.DocuSealSubmissionId = s1.SubmitterId;
                mockSubmission.DocuSealSubmitUrl = s1.EmbedUrl;
                mockSubmission.Status = ComplianceSubmissionStatus.Pending;
                await db.SaveChangesAsync(ct);

                result = new ComplianceForms.FillAndSubmitResult(
                    mockSubmission.Id,
                    s1.EmbedUrl,
                    isI9Template,
                    s2?.SubmitterId);
            }
            else
            {
                result = await mediator.Send(
                    new ComplianceForms.FillAndSubmitFormForSigningCommand(
                        request.UserId,
                        template.Id,
                        formDataJson,
                        request.UserEmail,
                        request.UserName),
                    ct);
            }

            signingUrls.Add(new OnboardingSigningUrlModel(
                template.FormType.ToString(),
                template.Name,
                result.EmployeeEmbedUrl,
                result.SubmissionId));

            if (result.IsI9)
                i9EmployerSubmitterId = result.EmployerDocuSealSubmitterId;
        }

        // ── 4. Persist profile data ──────────────────────────────────────────
        await UpsertProfileAsync(request.UserId, m, ct);

        // ── 5. Link identity documents uploaded during wizard ────────────────
        await SaveIdentityDocumentsAsync(request.UserId, m, ct);

        // ── 6. Mark acknowledgments ──────────────────────────────────────────
        if (m.AcknowledgeWorkersComp || m.AcknowledgeHandbook)
        {
            await MarkAcknowledgmentsAsync(request.UserId, m.AcknowledgeWorkersComp, m.AcknowledgeHandbook, ct);
        }

        return new OnboardingSubmitResultModel(
            RequiresSigning: signingUrls.Count > 0,
            SigningUrls: signingUrls,
            I9EmployerDocuSealSubmitterId: i9EmployerSubmitterId);
    }

    private async Task UpsertProfileAsync(int userId, OnboardingSubmitRequestModel m, CancellationToken ct)
    {
        var profile = await db.EmployeeProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null)
        {
            profile = new Core.Entities.EmployeeProfile { UserId = userId };
            db.EmployeeProfiles.Add(profile);
        }

        // Personal
        profile.PhoneNumber = m.Phone;
        profile.PersonalEmail = m.Email;
        profile.DateOfBirth = m.DateOfBirth;

        // Address
        profile.Street1 = m.Street1;
        profile.Street2 = m.Street2;
        profile.City = m.City;
        profile.State = m.AddressState;
        profile.ZipCode = m.ZipCode;
        profile.Country = "US";

        // Direct deposit completion
        if (!string.IsNullOrWhiteSpace(m.BankName) && !string.IsNullOrWhiteSpace(m.RoutingNumber))
            profile.DirectDepositCompletedAt ??= DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        // Update ASP.NET Identity user's name fields if blank
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is not null)
        {
            if (string.IsNullOrWhiteSpace(user.FirstName))
                user.FirstName = m.FirstName;
            if (string.IsNullOrWhiteSpace(user.LastName))
                user.LastName = m.LastName;
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task SaveIdentityDocumentsAsync(int userId, OnboardingSubmitRequestModel m, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(m.I9DocumentChoice))
            return;

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

            var identityDoc = new IdentityDocument
            {
                UserId = userId,
                DocumentType = docType,
                FileAttachmentId = attachmentId.Value,
                ExpiresAt = expiresAt.HasValue ? expiresAt.Value : null,
            };
            db.IdentityDocuments.Add(identityDoc);
            await db.SaveChangesAsync(ct);

            // Back-fill EntityId on the pre-uploaded FileAttachment now that we have an IdentityDocument.Id
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
            ?? new Core.Entities.EmployeeProfile { UserId = userId };

        var now = DateTimeOffset.UtcNow;
        if (workersComp)
            profile.WorkersCompAcknowledgedAt = now;
        if (handbook)
            profile.HandbookAcknowledgedAt = now;

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Builds the canonical flat JSON dictionary used for AcroForm field mapping.
    /// Keys here are the logical names that admins reference when configuring AcroFieldMapJson.
    /// Shared with <see cref="PreviewOnboardingPdfHandler"/> and <see cref="SignOnboardingFormHandler"/>.
    /// </summary>
    internal static Dictionary<string, string> BuildFormDataDictionary(OnboardingSubmitRequestModel m)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Shared personal
            ["firstName"] = m.FirstName,
            ["middleName"] = m.MiddleName ?? string.Empty,
            ["lastName"] = m.LastName,
            ["otherLastNames"] = m.OtherLastNames ?? string.Empty,
            ["fullName"] = BuildFullName(m),
            ["dateOfBirth"] = m.DateOfBirth.ToString("MM/dd/yyyy"),
            ["ssn"] = m.Ssn,
            ["ssnDash"] = FormatSsn(m.Ssn),
            ["email"] = m.Email,
            ["phone"] = m.Phone,

            // Shared address
            ["street1"] = m.Street1,
            ["street2"] = m.Street2 ?? string.Empty,
            ["city"] = m.City,
            ["addressState"] = m.AddressState,
            ["zipCode"] = m.ZipCode,
            ["cityStateZip"] = $"{m.City}, {m.AddressState} {m.ZipCode}",

            // W-4 specific
            ["w4FilingStatus"] = m.W4FilingStatus,
            ["w4MultipleJobs"] = m.W4MultipleJobs ? "true" : string.Empty,
            ["w4ClaimDependentsAmount"] = m.W4ClaimDependentsAmount > 0
                ? m.W4ClaimDependentsAmount.ToString("F2") : string.Empty,
            ["w4OtherIncome"] = m.W4OtherIncome > 0
                ? m.W4OtherIncome.ToString("F2") : string.Empty,
            ["w4Deductions"] = m.W4Deductions > 0
                ? m.W4Deductions.ToString("F2") : string.Empty,
            ["w4ExtraWithholding"] = m.W4ExtraWithholding > 0
                ? m.W4ExtraWithholding.ToString("F2") : string.Empty,
            ["w4ExemptFromWithholding"] = m.W4ExemptFromWithholding ? "Exempt" : string.Empty,

            // State withholding specific
            ["stateFilingStatus"] = m.StateFilingStatus ?? string.Empty,
            ["stateAllowances"] = m.StateAllowances?.ToString() ?? string.Empty,
            ["stateAdditionalWithholding"] = m.StateAdditionalWithholding?.ToString("F2") ?? string.Empty,
            ["stateExempt"] = m.StateExempt == true ? "Exempt" : string.Empty,

            // I-9 specific
            ["i9CitizenshipStatus"] = m.I9CitizenshipStatus,
            ["i9AlienRegNumber"] = m.I9AlienRegNumber ?? string.Empty,
            ["i9I94Number"] = m.I9I94Number ?? string.Empty,
            ["i9ForeignPassportNumber"] = m.I9ForeignPassportNumber ?? string.Empty,
            ["i9ForeignPassportCountry"] = m.I9ForeignPassportCountry ?? string.Empty,
            ["i9WorkAuthExpiry"] = m.I9WorkAuthExpiry?.ToString("MM/dd/yyyy") ?? string.Empty,
            ["i9PreparedByPreparer"] = m.I9PreparedByPreparer ? "true" : string.Empty,
            ["i9PreparerFirstName"] = m.I9PreparerFirstName ?? string.Empty,
            ["i9PreparerLastName"] = m.I9PreparerLastName ?? string.Empty,
            ["i9PreparerAddress"] = m.I9PreparerAddress ?? string.Empty,
            ["i9PreparerCity"] = m.I9PreparerCity ?? string.Empty,
            ["i9PreparerState"] = m.I9PreparerState ?? string.Empty,
            ["i9PreparerZip"] = m.I9PreparerZip ?? string.Empty,

            // Direct deposit
            ["bankName"] = m.BankName,
            ["routingNumber"] = m.RoutingNumber,
            ["accountNumber"] = m.AccountNumber,
            ["accountType"] = m.AccountType,
        };

        // W-4 checkbox helpers (some PDFs use separate fields per filing status)
        d["w4Single"] = m.W4FilingStatus == "Single" ? "Yes" : string.Empty;
        d["w4MFJ"] = m.W4FilingStatus == "MFJ" ? "Yes" : string.Empty;
        d["w4MFS"] = m.W4FilingStatus == "MFS" ? "Yes" : string.Empty;
        d["w4HH"] = m.W4FilingStatus == "HH" ? "Yes" : string.Empty;

        // I-9 checkbox helpers
        d["i9UsCitizen"] = m.I9CitizenshipStatus == "1" ? "Yes" : string.Empty;
        d["i9NoncitizenNational"] = m.I9CitizenshipStatus == "2" ? "Yes" : string.Empty;
        d["i9LPR"] = m.I9CitizenshipStatus == "3" ? "Yes" : string.Empty;
        d["i9AlienAuthorized"] = m.I9CitizenshipStatus == "4" ? "Yes" : string.Empty;

        return d;
    }

    internal static string BuildFullName(OnboardingSubmitRequestModel m)
    {
        if (!string.IsNullOrWhiteSpace(m.MiddleName))
            return $"{m.FirstName} {m.MiddleName} {m.LastName}";
        return $"{m.FirstName} {m.LastName}";
    }

    public static string FormatSsn(string raw)
    {
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 9)
            return $"{digits[..3]}-{digits[3..5]}-{digits[5..]}";
        return raw;
    }
}
