using System.Text.Json;

using MediatR;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Onboarding;

/// <summary>
/// Fills a single compliance form PDF and creates a DocuSeal signing submission for it.
///
/// This is the per-form sign step in the new review flow:
///   save → [preview-pdf → sign-form] × N → done
///
/// Behaviour by configuration:
///   • Template has AcroFieldMapJson + FilledPdfTemplate → fill PDF → DocuSeal submission
///   • MockIntegrations=true → DocuSeal mock (no PDF fill required if no template)
///   • Otherwise → returns a 400 so the frontend can show "not configured" message
/// </summary>
public record SignOnboardingFormCommand(
    int UserId,
    string UserEmail,
    string UserName,
    SignOnboardingFormRequestModel Model) : IRequest<SignOnboardingFormResultModel>;

public class SignOnboardingFormHandler(
    AppDbContext db,
    IStorageService storageService,
    IPdfFormFillService pdfFormFillService,
    IDocumentSigningService signingService,
    IOptions<MinioOptions> minioOptions,
    IConfiguration configuration,
    IPiiProtector pii)
    : IRequestHandler<SignOnboardingFormCommand, SignOnboardingFormResultModel>
{
    private bool IsMock => configuration.GetValue<bool>("MockIntegrations");

    public async Task<SignOnboardingFormResultModel> Handle(
        SignOnboardingFormCommand request, CancellationToken ct)
    {
        if (!Enum.TryParse<ComplianceFormType>(request.Model.FormType, out var formType))
            throw new KeyNotFoundException($"Unknown form type: {request.Model.FormType}");

        var template = await db.ComplianceFormTemplates
            .Include(t => t.FilledPdfTemplate)
            .FirstOrDefaultAsync(t => t.IsActive && t.FormType == formType, ct)
            ?? throw new KeyNotFoundException($"No active template found for form type {formType}.");

        var m = request.Model.FormData;
        var isI9 = formType == ComplianceFormType.I9;
        var templateName = $"{template.Name} — {request.UserName} — {DateTimeOffset.UtcNow:yyyyMMdd}";

        IReadOnlyList<SequentialSubmitter> submitters = isI9
            ? [
                new SequentialSubmitter(1, request.UserEmail, request.UserName, "Employee"),
                new SequentialSubmitter(2, "employer@placeholder.local", "Employer", "Employer"),
              ]
            : [new SequentialSubmitter(1, request.UserEmail, request.UserName, "Employee")];

        bool hasFullTemplate = !string.IsNullOrWhiteSpace(template.AcroFieldMapJson)
                               && template.FilledPdfTemplate is not null;

        if (hasFullTemplate)
        {
            // ── Full path: fill PDF → DocuSeal ──────────────────────────────
            var formData = SubmitOnboardingHandler.BuildFormDataDictionary(m);
            // Backfill SSN / bank from the encrypted draft so the filled PDF
            // gets the real values when the wizard intentionally sent them
            // blank (the user already supplied them earlier).
            await OnboardingPiiMerge.MergeStoredPiiAsync(db, pii, request.UserId, formData, ct);
            var formDataJson = JsonSerializer.Serialize(formData);

            byte[] blankPdfBytes;
            using (var stream = await storageService.DownloadAsync(
                template.FilledPdfTemplate!.BucketName,
                template.FilledPdfTemplate.ObjectKey, ct))
            {
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, ct);
                blankPdfBytes = ms.ToArray();
            }

            var fieldValues = BuildFieldValues(template.AcroFieldMapJson!, formDataJson);

            // I-9: don't flatten so employer can fill Section 2.  All others: flatten.
            var flatten = !isI9;
            var filledPdfBytes = await pdfFormFillService.FillFormAsync(blankPdfBytes, fieldValues, flatten, ct);

            // Store filled PDF in MinIO
            var opts = minioOptions.Value;
            var filledKey = $"compliance/{request.UserId}/{template.Id}/{Guid.NewGuid():N}-filled.pdf";
            using (var fs = new MemoryStream(filledPdfBytes))
                await storageService.UploadAsync(opts.PiiDocsBucket, filledKey, fs, "application/pdf", ct);

            var filledAttachment = new FileAttachment
            {
                FileName = $"{template.Name}-filled.pdf",
                ContentType = "application/pdf",
                Size = filledPdfBytes.Length,
                BucketName = opts.PiiDocsBucket,
                ObjectKey = filledKey,
                EntityType = "compliance_submissions",
                EntityId = 0,
                UploadedById = request.UserId,
                Sensitivity = "pii",
            };
            db.Set<FileAttachment>().Add(filledAttachment);
            await db.SaveChangesAsync(ct);

            var multiSub = await signingService.CreateSubmissionFromPdfAsync(templateName, filledPdfBytes, submitters, ct);

            if (!multiSub.SubmittersByOrder.TryGetValue(1, out var s1))
                throw new InvalidOperationException("DocuSeal returned no embed URL.");

            var submission = await UpsertSubmissionAsync(
                request.UserId, template.Id, formDataJson, s1.SubmitterId, s1.EmbedUrl, isI9, ct);

            filledAttachment.EntityId = submission.Id;
            await db.SaveChangesAsync(ct);

            return new SignOnboardingFormResultModel(s1.EmbedUrl, submission.Id, IsMock: false);
        }
        else if (IsMock)
        {
            // ── Mock path: no PDF fill, DocuSeal mock returns fake URLs ──────
            var multiSub = await signingService.CreateSubmissionFromPdfAsync(templateName, [], submitters, ct);

            if (!multiSub.SubmittersByOrder.TryGetValue(1, out var mockS1))
                throw new InvalidOperationException("Mock DocuSeal returned no submitter.");

            var mockSubmission = await UpsertSubmissionAsync(
                request.UserId, template.Id, string.Empty, mockS1.SubmitterId, mockS1.EmbedUrl, isI9, ct);

            return new SignOnboardingFormResultModel(mockS1.EmbedUrl, mockSubmission.Id, IsMock: true);
        }
        else
        {
            throw new InvalidOperationException(
                $"Template '{template.Name}' has not been configured for PDF pre-fill. " +
                "Upload a blank government PDF and configure AcroFieldMapJson in the admin panel.");
        }
    }

    private async Task<ComplianceFormSubmission> UpsertSubmissionAsync(
        int userId, int templateId, string formDataJson,
        int docuSealSubmitterId, string embedUrl, bool isI9,
        CancellationToken ct)
    {
        var submission = await db.ComplianceFormSubmissions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.TemplateId == templateId, ct);

        if (submission is null)
        {
            submission = new ComplianceFormSubmission
            {
                TemplateId = templateId,
                UserId = userId,
                Status = ComplianceSubmissionStatus.Pending,
            };
            db.ComplianceFormSubmissions.Add(submission);
        }

        // FormDataJson is a Postgres `json` column — empty string is not valid
        // JSON and the insert/update fails with 22P02 (reported 2026-05-10 from
        // the mock-DocuSeal path which passes string.Empty).
        submission.FormDataJson = string.IsNullOrWhiteSpace(formDataJson) ? "{}" : formDataJson;
        submission.DocuSealSubmissionId = docuSealSubmitterId;
        submission.DocuSealSubmitUrl = embedUrl;
        submission.Status = ComplianceSubmissionStatus.Pending;

        if (isI9)
            submission.I9Section2OverdueAt = AddBusinessDays(DateTimeOffset.UtcNow, 3);

        await db.SaveChangesAsync(ct);
        return submission;
    }

    private static Dictionary<string, string> BuildFieldValues(string acroFieldMapJson, string formDataJson)
    {
        var fieldValues = new Dictionary<string, string>();
        using var mapDoc = JsonDocument.Parse(acroFieldMapJson);
        using var dataDoc = JsonDocument.Parse(formDataJson);

        foreach (var mapping in mapDoc.RootElement.EnumerateObject())
        {
            var acroFieldName = mapping.Value.GetString();
            if (string.IsNullOrWhiteSpace(acroFieldName)) continue;
            if (dataDoc.RootElement.TryGetProperty(mapping.Name, out var value))
            {
                fieldValues[acroFieldName] = value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? string.Empty
                    : value.ToString();
            }
        }

        return fieldValues;
    }

    private static DateTimeOffset AddBusinessDays(DateTimeOffset date, int days)
    {
        var result = date;
        var added = 0;
        while (added < days)
        {
            result = result.AddDays(1);
            if (result.DayOfWeek != DayOfWeek.Saturday && result.DayOfWeek != DayOfWeek.Sunday)
                added++;
        }
        return result;
    }
}
