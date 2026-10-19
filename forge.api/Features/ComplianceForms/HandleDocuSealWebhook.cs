using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

public record HandleDocuSealWebhookCommand(
    int SubmissionId,
    string Status,
    DateTimeOffset? CompletedAt,
    /// <summary>
    /// true = one submitter finished (form.completed, or the older submitter.completed).
    /// false = a submission-level event (submission.completed, or a form.completed without a nested submission).
    /// </summary>
    bool IsSubmitterEvent = false,
    /// <summary>The DocuSeal submitter id, when the event names one.</summary>
    int? SubmitterId = null,
    /// <summary>true when every submitter has signed, so the final signed PDF is ready.</summary>
    bool AllSigned = false) : IRequest;

public class HandleDocuSealWebhookHandler(
    AppDbContext db,
    IDocumentSigningService signingService,
    IStorageService storageService,
    IOptions<MinioOptions> minioOptions,
    IMediator mediator,
    IClock clock,
    ILogger<HandleDocuSealWebhookHandler> logger)
    : IRequestHandler<HandleDocuSealWebhookCommand>
{
    public async Task Handle(HandleDocuSealWebhookCommand request, CancellationToken ct)
    {
        var submission = await db.ComplianceFormSubmissions
            .Include(s => s.Template)
            .FirstOrDefaultAsync(s => s.DocuSealSubmissionId == request.SubmissionId, ct);

        if (submission is null && request.SubmitterId is int submitterId && submitterId != request.SubmissionId)
        {
            submission = await db.ComplianceFormSubmissions
                .Include(s => s.Template)
                .FirstOrDefaultAsync(s => s.DocuSealSubmissionId == submitterId, ct);
            if (submission is not null)
                submission.DocuSealSubmissionId = request.SubmissionId;
        }

        if (submission is null)
        {
            logger.LogInformation(
                "DocuSeal webhook for submission {SubmissionId} matches no compliance submission; ignored",
                request.SubmissionId);
            return;
        }

        var isI9 = submission.Template.FormType == Forge.Core.Enums.ComplianceFormType.I9;
        var signedAt = request.CompletedAt ?? clock.UtcNow;
        var finished = request.Status == "completed"
            && (request.AllSigned || !request.IsSubmitterEvent || !isI9 || submission.I9Section1SignedAt is not null);

        if (isI9 && request.Status == "completed" && submission.I9Section1SignedAt is null)
        {
            submission.I9Section1SignedAt = signedAt;
            if (!finished)
            {
                submission.Status = ComplianceSubmissionStatus.Opened;
                await db.SaveChangesAsync(ct);
                return;
            }
        }

        if (request.Status != "completed")
        {
            submission.Status = request.Status switch
            {
                "opened" => ComplianceSubmissionStatus.Opened,
                "expired" => ComplianceSubmissionStatus.Expired,
                "declined" => ComplianceSubmissionStatus.Declined,
                _ => submission.Status,
            };
            await db.SaveChangesAsync(ct);
            return;
        }

        if (submission.Status == ComplianceSubmissionStatus.Completed && submission.SignedPdfFileId is not null)
            return;

        // ── I-9 Section 2: all submitters signed — capture Section 2 timestamp ──────────
        if (isI9 && submission.I9Section2SignedAt is null)
            submission.I9Section2SignedAt = signedAt;

        // Download signed PDF from DocuSeal
        var signedPdfBytes = await signingService.GetSignedPdfAsync(request.SubmissionId, ct);

        // Upload to MinIO (PII bucket)
        var opts = minioOptions.Value;
        var objectKey = $"compliance/{submission.UserId}/{submission.TemplateId}/{Guid.NewGuid():N}-signed.pdf";

        using var stream = new MemoryStream(signedPdfBytes);
        await storageService.UploadAsync(opts.PiiDocsBucket, objectKey, stream, "application/pdf", ct);

        // Create FileAttachment record
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

        // Update submission
        submission.Status = ComplianceSubmissionStatus.Completed;
        submission.SignedAt = signedAt;
        submission.SignedPdfFileId = fileAttachment.Id;

        await db.SaveChangesAsync(ct);

        // Acknowledge form completion on EmployeeProfile
        if (!string.IsNullOrEmpty(submission.Template.ProfileCompletionKey))
        {
            await mediator.Send(
                new EmployeeProfile.AcknowledgeFormCommand(
                    submission.UserId, submission.Template.ProfileCompletionKey),
                ct);
        }
    }
}
