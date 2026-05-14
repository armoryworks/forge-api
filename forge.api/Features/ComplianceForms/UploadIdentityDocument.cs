using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ComplianceForms;

public record UploadIdentityDocumentCommand(
    int UserId,
    IdentityDocumentType DocumentType,
    DateTimeOffset? ExpiresAt,
    int FileAttachmentId) : IRequest<IdentityDocumentResponseModel>;

public class UploadIdentityDocumentHandler(AppDbContext db)
    : IRequestHandler<UploadIdentityDocumentCommand, IdentityDocumentResponseModel>
{
    public async Task<IdentityDocumentResponseModel> Handle(
        UploadIdentityDocumentCommand request, CancellationToken ct)
    {
        var fileAttachment = await db.Set<FileAttachment>()
            .FirstOrDefaultAsync(f => f.Id == request.FileAttachmentId, ct)
            ?? throw new KeyNotFoundException($"File attachment {request.FileAttachmentId} not found.");

        fileAttachment.Sensitivity = "pii";

        var document = new IdentityDocument
        {
            UserId = request.UserId,
            DocumentType = request.DocumentType,
            FileAttachmentId = request.FileAttachmentId,
            ExpiresAt = request.ExpiresAt,
        };

        db.IdentityDocuments.Add(document);
        await db.SaveChangesAsync(ct);

        return new IdentityDocumentResponseModel(
            document.Id, document.UserId, document.DocumentType,
            document.FileAttachmentId, fileAttachment.FileName,
            document.VerifiedAt, document.VerifiedById, null,
            document.ExpiresAt, document.Notes, document.CreatedAt
        );
    }
}
