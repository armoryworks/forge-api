using System.Text.Json;

using FluentValidation;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

public record SubmitFormDataCommand(
    int UserId,
    int TemplateId,
    string FormDataJson,
    int? FormDefinitionVersionId = null,
    string? UserEmail = null,
    string? UserName = null) : IRequest<ComplianceFormSubmissionResponseModel>;

public class SubmitFormDataHandler(AppDbContext db, IMediator mediator)
    : IRequestHandler<SubmitFormDataCommand, ComplianceFormSubmissionResponseModel>
{
    public async Task<ComplianceFormSubmissionResponseModel> Handle(
        SubmitFormDataCommand request, CancellationToken ct)
    {
        var template = await db.ComplianceFormTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TemplateId, ct)
            ?? throw new KeyNotFoundException($"Compliance template {request.TemplateId} not found.");

        // Route through PDF fill + DocuSeal signing when AcroFieldMapJson is configured
        if (!string.IsNullOrWhiteSpace(template.AcroFieldMapJson)
            && template.FilledPdfTemplateId.HasValue
            && !string.IsNullOrWhiteSpace(request.UserEmail))
        {
            await mediator.Send(new FillAndSubmitFormForSigningCommand(
                request.UserId,
                request.TemplateId,
                request.FormDataJson,
                request.UserEmail,
                request.UserName ?? request.UserEmail), ct);

            // Re-load updated submission for response
            var updated = await db.ComplianceFormSubmissions
                .Include(s => s.Template)
                .FirstOrDefaultAsync(s => s.UserId == request.UserId && s.TemplateId == request.TemplateId, ct)!;

            return new ComplianceFormSubmissionResponseModel(
                updated!.Id, updated.TemplateId, template.Name,
                template.FormType, updated.Status, updated.SignedAt,
                updated.SignedPdfFileId, updated.DocuSealSubmitUrl,
                updated.FormDataJson, updated.FormDefinitionVersionId,
                updated.CreatedAt,
                updated.FilledPdfFileId,
                updated.I9Section1SignedAt, updated.I9Section2SignedAt,
                updated.I9EmployerUserId, updated.I9DocumentListType,
                updated.I9DocumentDataJson, updated.I9Section2OverdueAt,
                updated.I9ReverificationDueAt
            );
        }

        // Validate required fields against form definition
        var versionId = request.FormDefinitionVersionId;
        FormDefinitionVersion? version = null;
        if (versionId.HasValue)
        {
            version = await db.FormDefinitionVersions
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == versionId.Value, ct);
        }
        // Fall back to latest active version for this template
        version ??= await db.FormDefinitionVersions
            .AsNoTracking()
            .Where(v => v.TemplateId == request.TemplateId && v.IsActive && v.ExpirationDate == null)
            .OrderByDescending(v => v.EffectiveDate)
            .FirstOrDefaultAsync(ct);

        if (version is not null)
        {
            var missingFields = ValidateRequiredFields(version.FormDefinitionJson, request.FormDataJson);
            if (missingFields.Count > 0)
            {
                throw new ValidationException(
                    missingFields.Select(f => new FluentValidation.Results.ValidationFailure(f.Id, $"{f.Label} is required")));
            }
        }

        // Find or create submission
        var submission = await db.ComplianceFormSubmissions
            .FirstOrDefaultAsync(s => s.UserId == request.UserId && s.TemplateId == request.TemplateId, ct);

        if (submission is null)
        {
            submission = new ComplianceFormSubmission
            {
                TemplateId = request.TemplateId,
                UserId = request.UserId,
                Status = ComplianceSubmissionStatus.Completed,
                FormDataJson = request.FormDataJson,
                FormDefinitionVersionId = request.FormDefinitionVersionId ?? version?.Id,
                SignedAt = DateTimeOffset.UtcNow,
            };
            db.ComplianceFormSubmissions.Add(submission);
        }
        else
        {
            submission.FormDataJson = request.FormDataJson;
            submission.Status = ComplianceSubmissionStatus.Completed;
            submission.SignedAt = DateTimeOffset.UtcNow;
            if (request.FormDefinitionVersionId != null)
                submission.FormDefinitionVersionId = request.FormDefinitionVersionId;
        }

        await db.SaveChangesAsync(ct);

        // Mark the form as completed on the employee profile
        if (!string.IsNullOrEmpty(template.ProfileCompletionKey))
        {
            await mediator.Send(
                new EmployeeProfile.AcknowledgeFormCommand(
                    request.UserId, template.ProfileCompletionKey),
                ct);
        }

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

    private record MissingField(string Id, string Label);

    /// <summary>
    /// Parse the form definition JSON, find all fields with required=true,
    /// then check the submitted form data JSON for missing/empty values.
    /// </summary>
    private static List<MissingField> ValidateRequiredFields(string formDefinitionJson, string formDataJson)
    {
        var missing = new List<MissingField>();

        try
        {
            using var defDoc = JsonDocument.Parse(formDefinitionJson);
            using var dataDoc = JsonDocument.Parse(formDataJson);
            var data = dataDoc.RootElement;

            // Walk pages → sections → fields looking for required fields
            if (defDoc.RootElement.TryGetProperty("pages", out var pages))
            {
                foreach (var page in pages.EnumerateArray())
                {
                    if (!page.TryGetProperty("sections", out var sections)) continue;
                    foreach (var section in sections.EnumerateArray())
                    {
                        if (!section.TryGetProperty("fields", out var fields)) continue;
                        foreach (var field in fields.EnumerateArray())
                        {
                            if (!field.TryGetProperty("required", out var req) || req.ValueKind != JsonValueKind.True) continue;
                            if (!field.TryGetProperty("id", out var idProp)) continue;

                            var fieldId = idProp.GetString();
                            if (string.IsNullOrEmpty(fieldId)) continue;

                            var label = field.TryGetProperty("label", out var lbl)
                                ? lbl.GetString() ?? fieldId
                                : fieldId;

                            // Check if the value exists and is non-empty in submitted data
                            if (!data.TryGetProperty(fieldId, out var val)
                                || val.ValueKind == JsonValueKind.Null
                                || (val.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(val.GetString())))
                            {
                                missing.Add(new MissingField(fieldId, label));
                            }
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
            // If we can't parse, skip validation — don't block submission on malformed definitions
        }

        return missing;
    }
}
