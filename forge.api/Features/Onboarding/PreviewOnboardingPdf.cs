using System.Text.Json;

using MediatR;

using Microsoft.EntityFrameworkCore;
using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Onboarding;

/// <summary>
/// Fills a single compliance form PDF with the employee's data and returns the
/// result as a base64 string for inline browser preview.
///
/// No DocuSeal interaction, no DB writes — pure preview only.
/// When the template has no AcroFieldMapJson / blank PDF configured,
/// returns <see cref="PreviewOnboardingPdfResultModel.HasTemplate"/> = false
/// so the frontend can skip the preview step for that form.
/// </summary>
public record PreviewOnboardingPdfCommand(
    int UserId,
    PreviewOnboardingPdfRequestModel Model) : IRequest<PreviewOnboardingPdfResultModel>;

public class PreviewOnboardingPdfHandler(
    AppDbContext db,
    IStorageService storageService,
    IPdfFormFillService pdfFormFillService,
    IPiiProtector pii)
    : IRequestHandler<PreviewOnboardingPdfCommand, PreviewOnboardingPdfResultModel>
{
    public async Task<PreviewOnboardingPdfResultModel> Handle(
        PreviewOnboardingPdfCommand request, CancellationToken ct)
    {
        if (!Enum.TryParse<ComplianceFormType>(request.Model.FormType, out var formType))
            throw new KeyNotFoundException($"Unknown form type: {request.Model.FormType}");

        var template = await db.ComplianceFormTemplates
            .Include(t => t.FilledPdfTemplate)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.IsActive && t.FormType == formType, ct);

        // No template or not configured for PDF fill → frontend skips the preview
        if (template is null
            || string.IsNullOrWhiteSpace(template.AcroFieldMapJson)
            || template.FilledPdfTemplate is null)
        {
            return new PreviewOnboardingPdfResultModel(HasTemplate: false, PdfBase64: null);
        }

        // Build form-data JSON the same way the submit handler does
        var formData = SubmitOnboardingHandler.BuildFormDataDictionary(request.Model.FormData);
        // Backfill SSN / bank from the encrypted draft so the preview renders
        // with the right values when the wizard sent those fields blank.
        await OnboardingPiiMerge.MergeStoredPiiAsync(db, pii, request.UserId, formData, ct);
        var formDataJson = JsonSerializer.Serialize(formData);

        // Load blank government PDF from MinIO
        byte[] blankPdfBytes;
        using (var stream = await storageService.DownloadAsync(
            template.FilledPdfTemplate.BucketName,
            template.FilledPdfTemplate.ObjectKey, ct))
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            blankPdfBytes = ms.ToArray();
        }

        // Map logical keys → AcroForm field names and fill
        var fieldValues = BuildFieldValues(template.AcroFieldMapJson, formDataJson);

        // Always flatten for preview so it renders as a read-only document
        var filledPdf = await pdfFormFillService.FillFormAsync(blankPdfBytes, fieldValues, flatten: true, ct);

        return new PreviewOnboardingPdfResultModel(
            HasTemplate: true,
            PdfBase64: Convert.ToBase64String(filledPdf));
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
}
