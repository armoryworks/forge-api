using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

public record HandleDocuSealWebhookCommand(
    int SubmissionId,
    string Status,
    DateTimeOffset? CompletedAt,
    /// <summary>
    /// true = individual submitter completed (I-9 Section 1 by employee).
    /// false = all submitters completed (form.completed — final signed PDF ready).
    /// </summary>
    bool IsSubmitterEvent = false) : IRequest;

public class HandleDocuSealWebhookHandler(
    AppDbContext db,
    IDocumentSigningService signingService,
    IStorageService storageService,
    IOptions<MinioOptions> minioOptions,
    IMediator mediator)
    : IRequestHandler<HandleDocuSealWebhookCommand>
{
    public async Task Handle(HandleDocuSealWebhookCommand request, CancellationToken ct)
    {
        var submission = await db.ComplianceFormSubmissions
            .Include(s => s.Template)
            .FirstOrDefaultAsync(s => s.DocuSealSubmissionId == request.SubmissionId, ct)
            ?? throw new KeyNotFoundException(
                $"Submission with DocuSeal ID {request.SubmissionId} not found.");

        // ── I-9 Section 1 submitter event (employee signed, employer has not yet) ──────
        if (request.IsSubmitterEvent
            && submission.Template.FormType == Forge.Platform.Enums.ComplianceFormType.I9
            && submission.I9Section1SignedAt is null)
        {
            submission.I9Section1SignedAt = request.CompletedAt ?? DateTimeOffset.UtcNow;
            submission.Status = ComplianceSubmissionStatus.Opened;
            // Section 2 overdue deadline was set when submission was created — preserve it
            await db.SaveChangesAsync(ct);
            return;
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

        // ── I-9 Section 2: all submitters signed — capture Section 2 timestamp ──────────
        if (submission.Template.FormType == Forge.Platform.Enums.ComplianceFormType.I9)
        {
            submission.I9Section2SignedAt = request.CompletedAt ?? DateTimeOffset.UtcNow;
        }

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
        submission.SignedAt = request.CompletedAt ?? DateTimeOffset.UtcNow;
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
