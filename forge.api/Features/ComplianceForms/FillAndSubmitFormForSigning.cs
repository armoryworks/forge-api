using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

/// <summary>
/// Fills a government PDF (W-4, I-9, state withholding) with collected form data
/// using the template's AcroFieldMapJson, then submits to DocuSeal for signing.
///
/// Flow:
///   1. Load template + AcroFieldMapJson + FilledPdfTemplate (blank government PDF from MinIO)
///   2. Map dynamic form field values → AcroForm field names
///   3. Call IPdfFormFillService.FillFormAsync() with appropriate flatten strategy
///   4. Store filled (unsigned) PDF in MinIO → FileAttachment → FilledPdfFileId
///   5. Build submitter list (single for W-4/state, sequential for I-9)
///   6. Call IDocumentSigningService.CreateSubmissionFromPdfAsync()
///   7. Update submission with DocuSealSubmissionId + embed URL(s)
///   8. Return the employee-facing embed URL (Order=1)
/// </summary>
public record FillAndSubmitFormForSigningCommand(
    int UserId,
    int TemplateId,
    string FormDataJson,
    string UserEmail,
    string UserName) : IRequest<FillAndSubmitResult>;

public record FillAndSubmitResult(
    int SubmissionId,
    string EmployeeEmbedUrl,
    bool IsI9,
    /// <summary>For I-9 only: employer DocuSeal submitter ID to use when employer signs.</summary>
    int? EmployerDocuSealSubmitterId);

public class FillAndSubmitFormForSigningHandler(
    AppDbContext db,
    IStorageService storageService,
    IPdfFormFillService pdfFormFillService,
    IDocumentSigningService signingService,
    IOptions<MinioOptions> minioOptions)
    : IRequestHandler<FillAndSubmitFormForSigningCommand, FillAndSubmitResult>
{
    public async Task<FillAndSubmitResult> Handle(
        FillAndSubmitFormForSigningCommand request, CancellationToken ct)
    {
        var template = await db.ComplianceFormTemplates
            .Include(t => t.FilledPdfTemplate)
            .FirstOrDefaultAsync(t => t.Id == request.TemplateId, ct)
            ?? throw new KeyNotFoundException($"Compliance template {request.TemplateId} not found.");

        if (string.IsNullOrWhiteSpace(template.AcroFieldMapJson))
            throw new InvalidOperationException(
                $"Template {request.TemplateId} has no AcroFieldMapJson configured for PDF fill.");

        if (template.FilledPdfTemplate is null)
            throw new InvalidOperationException(
                $"Template {request.TemplateId} has no FilledPdfTemplate configured.");

        // Load blank government PDF from MinIO
        var opts = minioOptions.Value;
        byte[] blankPdfBytes;
        using (var pdfStream = await storageService.DownloadAsync(
            template.FilledPdfTemplate.BucketName,
            template.FilledPdfTemplate.ObjectKey, ct))
        {
            using var ms = new MemoryStream();
            await pdfStream.CopyToAsync(ms, ct);
            blankPdfBytes = ms.ToArray();
        }

        // Build AcroForm field values from collected form data
        var fieldValues = BuildFieldValues(template.AcroFieldMapJson, request.FormDataJson);

        var isI9 = template.FormType == ComplianceFormType.I9;

        // For I-9: don't flatten (employer needs Section 2 fields editable)
        // For W-4, state withholding: flatten (single-party signing, no further fills)
        var flatten = !isI9;

        var filledPdfBytes = await pdfFormFillService.FillFormAsync(blankPdfBytes, fieldValues, flatten, ct);

        // Store filled (unsigned) PDF in MinIO
        var filledKey = $"compliance/{request.UserId}/{request.TemplateId}/{Guid.NewGuid():N}-filled.pdf";
        using (var filledStream = new MemoryStream(filledPdfBytes))
        {
            await storageService.UploadAsync(
                opts.PiiDocsBucket, filledKey, filledStream, "application/pdf", ct);
        }

        var filledAttachment = new FileAttachment
        {
            FileName = $"{template.Name}-filled.pdf",
            ContentType = "application/pdf",
            Size = filledPdfBytes.Length,
            BucketName = opts.PiiDocsBucket,
            ObjectKey = filledKey,
            EntityType = "compliance_submissions",
            EntityId = 0, // updated after submission record created
            UploadedById = request.UserId,
            Sensitivity = "pii",
        };
        db.Set<FileAttachment>().Add(filledAttachment);
        await db.SaveChangesAsync(ct);

        // Build submitter list
        IReadOnlyList<SequentialSubmitter> submitters = isI9
            ? BuildI9Submitters(request)
            : [new SequentialSubmitter(1, request.UserEmail, request.UserName, "Employee")];

        // Submit to DocuSeal
        var templateName = $"{template.Name} — {request.UserName} — {DateTimeOffset.UtcNow:yyyyMMdd}";
        var multiSubmission = await signingService.CreateSubmissionFromPdfAsync(
            templateName, filledPdfBytes, submitters, ct);

        var order1Result = multiSubmission.SubmittersByOrder.TryGetValue(1, out var s1) ? s1 : null;
        var order2Result = isI9 && multiSubmission.SubmittersByOrder.TryGetValue(2, out var s2) ? s2 : null;

        if (order1Result is null)
            throw new InvalidOperationException("DocuSeal returned no embed URL for the first submitter.");

        // Upsert submission record
        var submission = await db.ComplianceFormSubmissions
            .FirstOrDefaultAsync(s => s.UserId == request.UserId && s.TemplateId == request.TemplateId, ct);

        if (submission is null)
        {
            submission = new ComplianceFormSubmission
            {
                TemplateId = request.TemplateId,
                UserId = request.UserId,
                Status = ComplianceSubmissionStatus.Pending,
            };
            db.ComplianceFormSubmissions.Add(submission);
        }

        submission.FormDataJson = request.FormDataJson;
        submission.DocuSealSubmissionId = order1Result.SubmitterId;
        submission.DocuSealSubmitUrl = order1Result.EmbedUrl;
        submission.FilledPdfFileId = filledAttachment.Id;
        submission.Status = ComplianceSubmissionStatus.Pending;

        if (isI9)
        {
            // Compute Section 2 deadline: 3 business days from now (approximated as 3 calendar days + weekends)
            submission.I9Section2OverdueAt = AddBusinessDays(DateTimeOffset.UtcNow, 3);
        }

        await db.SaveChangesAsync(ct);

        // Fix the EntityId on the file attachment now that we have the submission ID
        filledAttachment.EntityId = submission.Id;
        await db.SaveChangesAsync(ct);

        return new FillAndSubmitResult(
            submission.Id,
            order1Result.EmbedUrl,
            isI9,
            order2Result?.SubmitterId);
    }

    private static Dictionary<string, string> BuildFieldValues(
        string acroFieldMapJson, string formDataJson)
    {
        var fieldValues = new Dictionary<string, string>();

        using var mapDoc = JsonDocument.Parse(acroFieldMapJson);
        using var dataDoc = JsonDocument.Parse(formDataJson);

        // AcroFieldMapJson structure: { "dynamicFieldId": "AcroFormFieldName", ... }
        foreach (var mapping in mapDoc.RootElement.EnumerateObject())
        {
            var dynamicFieldId = mapping.Name;
            var acroFieldName = mapping.Value.GetString();
            if (string.IsNullOrWhiteSpace(acroFieldName)) continue;

            if (dataDoc.RootElement.TryGetProperty(dynamicFieldId, out var value))
            {
                var strValue = value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? string.Empty
                    : value.ToString();
                fieldValues[acroFieldName] = strValue;
            }
        }

        return fieldValues;
    }

    private static IReadOnlyList<SequentialSubmitter> BuildI9Submitters(
        FillAndSubmitFormForSigningCommand request)
    {
        return
        [
            new SequentialSubmitter(1, request.UserEmail, request.UserName, "Employee"),
            // Employer (order=2) — email is a placeholder; DocuSeal will not auto-send email
            // The employer accesses DocuSeal via the admin UI when they're ready to sign Section 2
            new SequentialSubmitter(2, "employer@placeholder.local", "Employer", "Employer"),
        ];
    }

    private static DateTimeOffset AddBusinessDays(DateTimeOffset date, int businessDays)
    {
        var result = date;
        var added = 0;
        while (added < businessDays)
        {
            result = result.AddDays(1);
            if (result.DayOfWeek != DayOfWeek.Saturday && result.DayOfWeek != DayOfWeek.Sunday)
                added++;
        }
        return result;
    }
}
