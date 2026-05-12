using System.Text.Json;

using FluentValidation;

using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

/// <summary>
/// Request model for employer completing I-9 Section 2.
/// </summary>
public record CompleteI9Section2RequestModel(
    /// <summary>"A" or "B+C"</summary>
    string DocumentListType,
    /// <summary>
    /// JSON object containing document fields.
    /// For List A: { "listA_doc1_type", "listA_doc1_number", "listA_doc1_issuer", "listA_doc1_expiration", ... }
    /// For List B+C: { "listB_type", "listB_number", "listB_issuer", "listB_expiration",
    ///                  "listC_type", "listC_number", "listC_issuer", "listC_expiration" }
    /// Always includes "start_date" for first day of employment.
    /// </summary>
    string DocumentDataJson,
    /// <summary>Employee first day of work (ISO date).</summary>
    DateTimeOffset StartDate,
    /// <summary>
    /// Optional: date work authorisation expires for reverification tracking.
    /// Null for US citizens, LPRs, and documents with no expiration.
    /// </summary>
    DateTimeOffset? ReverificationDueAt);

public record CompleteI9Section2Command(
    int SubmissionId,
    int EmployerUserId,
    string DocumentListType,
    string DocumentDataJson,
    DateTimeOffset StartDate,
    DateTimeOffset? ReverificationDueAt) : IRequest<ComplianceFormSubmissionResponseModel>;

public class CompleteI9Section2Validator : AbstractValidator<CompleteI9Section2Command>
{
    public CompleteI9Section2Validator()
    {
        RuleFor(x => x.DocumentListType)
            .Must(v => v == "A" || v == "B+C")
            .WithMessage("DocumentListType must be 'A' or 'B+C'.");
        RuleFor(x => x.DocumentDataJson)
            .NotEmpty()
            .WithMessage("DocumentDataJson is required.");
        RuleFor(x => x.StartDate)
            .NotEmpty()
            .WithMessage("StartDate is required.");
    }
}

public class CompleteI9Section2Handler(
    AppDbContext db,
    IDocumentSigningService signingService,
    IOptions<MinioOptions> minioOptions,
    IStorageService storageService)
    : IRequestHandler<CompleteI9Section2Command, ComplianceFormSubmissionResponseModel>
{
    public async Task<ComplianceFormSubmissionResponseModel> Handle(
        CompleteI9Section2Command request, CancellationToken ct)
    {
        var submission = await db.ComplianceFormSubmissions
            .Include(s => s.Template)
            .FirstOrDefaultAsync(s => s.Id == request.SubmissionId, ct)
            ?? throw new KeyNotFoundException($"Submission {request.SubmissionId} not found.");

        if (submission.Template.FormType != ComplianceFormType.I9)
            throw new InvalidOperationException("CompleteI9Section2 only applies to I-9 submissions.");

        if (submission.I9Section1SignedAt is null)
            throw new InvalidOperationException("Section 1 must be signed before Section 2 can be completed.");

        if (submission.I9Section2SignedAt.HasValue)
            throw new InvalidOperationException("Section 2 has already been completed.");

        // Stamp the Section 2 completion fields
        submission.I9EmployerUserId = request.EmployerUserId;
        submission.I9DocumentListType = request.DocumentListType;
        submission.I9DocumentDataJson = request.DocumentDataJson;
        submission.I9Section2SignedAt = DateTimeOffset.UtcNow;
        submission.I9ReverificationDueAt = request.ReverificationDueAt;

        // If DocuSeal signing is pending, attempt to get the final signed PDF
        if (submission.DocuSealSubmissionId.HasValue && submission.SignedPdfFileId is null)
        {
            try
            {
                var signedPdfBytes = await signingService.GetSignedPdfAsync(
                    submission.DocuSealSubmissionId.Value, ct);

                if (signedPdfBytes.Length > 0)
                {
                    var opts = minioOptions.Value;
                    var objectKey =
                        $"compliance/{submission.UserId}/{submission.TemplateId}/{Guid.NewGuid():N}-signed.pdf";

                    using var stream = new MemoryStream(signedPdfBytes);
                    await storageService.UploadAsync(
                        opts.PiiDocsBucket, objectKey, stream, "application/pdf", ct);

                    var fileAttachment = new FileAttachment
                    {
                        FileName = $"{submission.Template.Name}-signed.pdf",
                        ContentType = "application/pdf",
                        Size = signedPdfBytes.Length,
                        BucketName = opts.PiiDocsBucket,
                        ObjectKey = objectKey,
                        EntityType = "compliance_submissions",
                        EntityId = submission.Id,
                        UploadedById = submission.UserId,
                        Sensitivity = "pii",
                    };

                    db.Set<FileAttachment>().Add(fileAttachment);
                    await db.SaveChangesAsync(ct);

                    submission.SignedPdfFileId = fileAttachment.Id;
                }
            }
            catch
            {
                // PDF not ready yet (employer may need to sign via DocuSeal embed) — continue
            }
        }

        submission.Status = ComplianceSubmissionStatus.Completed;
        submission.SignedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        return new ComplianceFormSubmissionResponseModel(
            submission.Id, submission.TemplateId, submission.Template.Name,
            submission.Template.FormType, submission.Status, submission.SignedAt,
            submission.SignedPdfFileId, submission.DocuSealSubmitUrl,
            submission.FormDataJson, submission.FormDefinitionVersionId,
            submission.CreatedAt,
            submission.FilledPdfFileId,
            submission.I9Section1SignedAt, submission.I9Section2SignedAt,
            submission.I9EmployerUserId, submission.I9DocumentListType,
            submission.I9DocumentDataJson, submission.I9Section2OverdueAt,
            submission.I9ReverificationDueAt);
    }
}
