using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Middleware;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Terms;

/// <summary>
/// S3 — full-replace update of a terms document. Scope/CustomerId/PartId are
/// immutable post-create. A BodyMarkdown change bumps <c>Version</c> so
/// snapshots (<c>QuoteTermsSnapshot.CompiledSource</c>) can point at the exact
/// wording that was sent. Rollup rule: one activity row listing all changed
/// fields, anchored on the scoped Customer/Part (company scope logs nothing —
/// see <see cref="CreateTermsDocumentHandler"/>).
/// </summary>
public record UpdateTermsDocumentCommand(int Id, UpdateTermsDocumentRequestModel Body, bool CallerIsAdmin)
    : IRequest<TermsDocumentResponseModel>;

public class UpdateTermsDocumentValidator : AbstractValidator<UpdateTermsDocumentCommand>
{
    public UpdateTermsDocumentValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Body.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Body.Summary).MaximumLength(1000);
        RuleFor(x => x.Body.BodyMarkdown).NotEmpty();
        RuleFor(x => x.Body.EffectiveFrom)
            .NotEqual(default(DateTimeOffset))
            .WithMessage("Effective From is required");
        RuleFor(x => x.Body)
            .Must(b => b.EffectiveTo == null || b.EffectiveTo > b.EffectiveFrom)
            .WithMessage("Effective To must be after Effective From");
    }
}

public class UpdateTermsDocumentHandler(AppDbContext db)
    : IRequestHandler<UpdateTermsDocumentCommand, TermsDocumentResponseModel>
{
    public async Task<TermsDocumentResponseModel> Handle(
        UpdateTermsDocumentCommand request, CancellationToken ct)
    {
        var doc = await db.TermsDocuments
            .Include(d => d.Customer)
            .Include(d => d.Part)
            .FirstOrDefaultAsync(d => d.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Terms document {request.Id} not found");

        if (doc.Scope == TermsScope.Company && !request.CallerIsAdmin)
            throw new ForbiddenException("Only administrators can manage company-scope terms");

        var body = request.Body;
        var changedFields = new List<string>();

        var title = body.Title.Trim();
        if (title != doc.Title) { doc.Title = title; changedFields.Add("title"); }

        var summary = string.IsNullOrWhiteSpace(body.Summary) ? null : body.Summary.Trim();
        if (summary != doc.Summary) { doc.Summary = summary; changedFields.Add("summary"); }

        if (body.BodyMarkdown != doc.BodyMarkdown)
        {
            doc.BodyMarkdown = body.BodyMarkdown;
            doc.Version += 1;
            changedFields.Add("bodyMarkdown");
        }
        if (body.EffectiveFrom != doc.EffectiveFrom) { doc.EffectiveFrom = body.EffectiveFrom; changedFields.Add("effectiveFrom"); }
        if (body.EffectiveTo != doc.EffectiveTo) { doc.EffectiveTo = body.EffectiveTo; changedFields.Add("effectiveTo"); }
        if (body.IsActive != doc.IsActive) { doc.IsActive = body.IsActive; changedFields.Add("isActive"); }
        if (body.SortOrder != doc.SortOrder) { doc.SortOrder = body.SortOrder; changedFields.Add("sortOrder"); }
        if (body.SourceFileAttachmentId != doc.SourceFileAttachmentId)
        {
            doc.SourceFileAttachmentId = body.SourceFileAttachmentId;
            changedFields.Add("sourceFileAttachmentId");
        }

        if (changedFields.Count > 0)
        {
            var description =
                $"Updated terms document \"{doc.Title}\" ({changedFields.Count} field{(changedFields.Count == 1 ? "" : "s")}: {string.Join(", ", changedFields)})";

            if (doc.CustomerId.HasValue)
                db.LogActivityAt("terms-document-updated", description, ("Customer", doc.CustomerId.Value));
            else if (doc.PartId.HasValue)
                db.LogActivityAt("terms-document-updated", description, ("Part", doc.PartId.Value));
            // Company scope: no anchor entity — see CreateTermsDocumentHandler.

            await db.SaveChangesAsync(ct);
        }

        return TermsDocumentMapper.ToResponseModel(doc);
    }
}
