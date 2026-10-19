using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Documents;

public record CreateDocumentRevisionCommand(int DocumentId, int FileAttachmentId, string ChangeDescription)
    : IRequest<DocumentRevisionResponseModel>;

public class CreateDocumentRevisionCommandValidator : AbstractValidator<CreateDocumentRevisionCommand>
{
    public CreateDocumentRevisionCommandValidator()
    {
        RuleFor(x => x.FileAttachmentId).GreaterThan(0).WithMessage("A file attachment is required.");
        RuleFor(x => x.ChangeDescription)
            .NotEmpty().WithMessage("Change description is required.")
            .MaximumLength(2000).WithMessage("Change description must not exceed 2000 characters.");
    }
}

public class CreateDocumentRevisionHandler(
    AppDbContext db,
    IHttpContextAccessor httpContextAccessor) : IRequestHandler<CreateDocumentRevisionCommand, DocumentRevisionResponseModel>
{
    public async Task<DocumentRevisionResponseModel> Handle(CreateDocumentRevisionCommand request, CancellationToken cancellationToken)
    {
        var userId = DocumentRevisionMapping.CurrentUserId(httpContextAccessor);

        var document = await db.ControlledDocuments
            .Include(d => d.Revisions)
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Document {request.DocumentId} not found.");

        if (document.Status == ControlledDocumentStatus.Obsolete)
            throw new InvalidOperationException("An obsolete document cannot be revised.");

        if (document.Revisions.Any(r => r.Status is DocumentRevisionStatus.Draft or DocumentRevisionStatus.InReview))
            throw new InvalidOperationException("The document already has a revision in draft or review.");

        var attachmentExists = await db.FileAttachments
            .AnyAsync(f => f.Id == request.FileAttachmentId, cancellationToken);
        if (!attachmentExists)
            throw new KeyNotFoundException($"File attachment {request.FileAttachmentId} not found.");

        var revision = new DocumentRevision
        {
            DocumentId = document.Id,
            RevisionNumber = document.Revisions.Count == 0 ? 1 : document.Revisions.Max(r => r.RevisionNumber) + 1,
            FileAttachmentId = request.FileAttachmentId,
            ChangeDescription = request.ChangeDescription.Trim(),
            AuthoredById = userId,
            Status = DocumentRevisionStatus.Draft,
        };

        db.DocumentRevisions.Add(revision);
        await db.SaveChangesAsync(cancellationToken);

        return DocumentRevisionMapping.ToResponse(revision);
    }
}
