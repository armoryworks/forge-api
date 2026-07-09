using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Forge.Api.Features.Files;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesOrders.Acceptance;

/// <summary>
/// Reconcile a pending e-signature acceptance with the provider. Polls the submission status; when the
/// customer has signed, downloads the signed PDF, stores it as the acceptance evidence (tagged
/// CustomerAcceptance) and flips the record to Accepted — which opens the production gate. Declined /
/// expired submissions are reflected too. Idempotent: a no-op on an already-terminal record.
/// </summary>
public record CompleteSignatureAcceptanceCommand(int SalesOrderId, int AcceptanceId)
    : IRequest<SalesOrderAcceptanceResponseModel>;

public class CompleteSignatureAcceptanceHandler(
    AppDbContext db, IDocumentSigningService signing, IStorageService storage,
    IOptions<MinioOptions> minioOptions, IClock clock)
    : IRequestHandler<CompleteSignatureAcceptanceCommand, SalesOrderAcceptanceResponseModel>
{
    public async Task<SalesOrderAcceptanceResponseModel> Handle(CompleteSignatureAcceptanceCommand request, CancellationToken cancellationToken)
    {
        var acceptance = await db.SalesOrderAcceptances
            .FirstOrDefaultAsync(a => a.Id == request.AcceptanceId && a.SalesOrderId == request.SalesOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Acceptance {request.AcceptanceId} not found on sales order {request.SalesOrderId}.");

        if (acceptance.Method != AcceptanceMethod.ESignature)
            throw new InvalidOperationException("Only an e-signature acceptance can be reconciled with the provider.");

        // Terminal already — nothing to poll.
        if (acceptance.Status is not AcceptanceStatus.Pending)
            return await AcceptanceQuery.ByIdAsync(db, acceptance.Id, cancellationToken);

        if (!int.TryParse(acceptance.ProviderReference, out var submissionId))
            throw new InvalidOperationException("Acceptance has no provider submission reference.");

        var status = await signing.GetSubmissionStatusAsync(submissionId, cancellationToken);

        switch (status.Status)
        {
            case "completed":
                var signedPdf = await signing.GetSignedPdfAsync(submissionId, cancellationToken);
                var bucket = FileEntityTypes.ResolveBucket("sales-orders", minioOptions.Value);
                var objectKey = $"sales-orders/{acceptance.SalesOrderId}/{Guid.NewGuid():N}-acceptance-signed.pdf";
                using (var stream = new MemoryStream(signedPdf))
                    await storage.UploadAsync(bucket, objectKey, stream, "application/pdf", cancellationToken);

                var file = new FileAttachment
                {
                    FileName = $"acceptance-signed-{acceptance.SalesOrderId}.pdf",
                    ContentType = "application/pdf",
                    Size = signedPdf.Length,
                    BucketName = bucket,
                    ObjectKey = objectKey,
                    EntityType = "sales-orders",
                    EntityId = acceptance.SalesOrderId,
                    UploadedById = db.CurrentUserId ?? 1,
                    DocumentType = "CustomerAcceptance",
                };
                db.Set<FileAttachment>().Add(file);
                await db.SaveChangesAsync(cancellationToken); // assign file.Id

                acceptance.Status = AcceptanceStatus.Accepted;
                acceptance.FileAttachmentId = file.Id;
                acceptance.AcceptedAt = status.CompletedAt ?? clock.UtcNow;
                db.LogActivityAt("so-acceptance-recorded",
                    "Customer e-signature completed", ("SalesOrder", acceptance.SalesOrderId));
                break;

            case "declined":
                acceptance.Status = AcceptanceStatus.Declined;
                db.LogActivityAt("so-acceptance-declined", "Customer declined e-signature", ("SalesOrder", acceptance.SalesOrderId));
                break;

            case "expired":
                acceptance.Status = AcceptanceStatus.Expired;
                break;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await AcceptanceQuery.ByIdAsync(db, acceptance.Id, cancellationToken);
    }
}
