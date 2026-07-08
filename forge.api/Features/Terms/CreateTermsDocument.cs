using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Terms;

/// <summary>
/// S3 — create a terms-and-conditions document. Company-scope documents are
/// Admin-only (controller passes <c>CallerIsAdmin</c>); customer/part scope is
/// open to the controller's Admin/Manager/OfficeManager roles.
///
/// Activity logging: TermsDocument is definitional master data. Scoped docs
/// anchor on their Customer/Part per the indexing-points rule. Company-scope
/// docs have no anchor entity — following the repo's global-settings
/// convention (SystemSetting mutations emit no ActivityLog row), they log
/// nothing.
/// </summary>
public record CreateTermsDocumentCommand(CreateTermsDocumentRequestModel Body, bool CallerIsAdmin)
    : IRequest<TermsDocumentResponseModel>;

public class CreateTermsDocumentValidator : AbstractValidator<CreateTermsDocumentCommand>
{
    public CreateTermsDocumentValidator()
    {
        RuleFor(x => x.Body.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Body.Summary).MaximumLength(1000);
        RuleFor(x => x.Body.BodyMarkdown).NotEmpty();
        RuleFor(x => x.Body.EffectiveFrom)
            .NotEqual(default(DateTimeOffset))
            .WithMessage("Effective From is required");
        RuleFor(x => x.Body)
            .Must(b => b.EffectiveTo == null || b.EffectiveTo > b.EffectiveFrom)
            .WithMessage("Effective To must be after Effective From");

        // Scope ↔ FK consistency: Customer scope requires CustomerId (and no
        // PartId), Part scope requires PartId (and no CustomerId), Company
        // scope forbids both.
        RuleFor(x => x.Body)
            .Must(b => b.Scope != TermsScope.Company || (b.CustomerId == null && b.PartId == null))
            .WithMessage("Company-scope terms must not reference a customer or part")
            .Must(b => b.Scope != TermsScope.Customer || (b.CustomerId != null && b.PartId == null))
            .WithMessage("Customer-scope terms require a CustomerId and must not reference a part")
            .Must(b => b.Scope != TermsScope.Part || (b.PartId != null && b.CustomerId == null))
            .WithMessage("Part-scope terms require a PartId and must not reference a customer");
    }
}

public class CreateTermsDocumentHandler(AppDbContext db)
    : IRequestHandler<CreateTermsDocumentCommand, TermsDocumentResponseModel>
{
    public async Task<TermsDocumentResponseModel> Handle(
        CreateTermsDocumentCommand request, CancellationToken ct)
    {
        var body = request.Body;

        if (body.Scope == TermsScope.Company && !request.CallerIsAdmin)
            throw new ForbiddenException("Only administrators can manage company-scope terms");

        if (body.CustomerId.HasValue
            && !await db.Customers.AnyAsync(c => c.Id == body.CustomerId.Value, ct))
        {
            throw new KeyNotFoundException($"Customer {body.CustomerId} not found");
        }
        if (body.PartId.HasValue
            && !await db.Parts.AnyAsync(p => p.Id == body.PartId.Value, ct))
        {
            throw new KeyNotFoundException($"Part {body.PartId} not found");
        }

        var doc = new TermsDocument
        {
            Scope = body.Scope,
            CustomerId = body.CustomerId,
            PartId = body.PartId,
            Title = body.Title.Trim(),
            Summary = string.IsNullOrWhiteSpace(body.Summary) ? null : body.Summary.Trim(),
            BodyMarkdown = body.BodyMarkdown,
            Version = 1,
            EffectiveFrom = body.EffectiveFrom,
            EffectiveTo = body.EffectiveTo,
            IsActive = body.IsActive,
            SortOrder = body.SortOrder,
            SourceFileAttachmentId = body.SourceFileAttachmentId,
        };
        db.TermsDocuments.Add(doc);

        if (doc.CustomerId.HasValue)
        {
            db.LogActivityAt("terms-document-added",
                $"Added terms document: {doc.Title}",
                ("Customer", doc.CustomerId.Value));
        }
        else if (doc.PartId.HasValue)
        {
            db.LogActivityAt("terms-document-added",
                $"Added terms document: {doc.Title}",
                ("Part", doc.PartId.Value));
        }
        // Company scope: no anchor entity — see class doc.

        await db.SaveChangesAsync(ct);

        return TermsDocumentMapper.ToResponseModel(doc);
    }
}
