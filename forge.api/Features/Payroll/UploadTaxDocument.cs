using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Payroll;

public record UploadTaxDocumentCommand(int UserId, UploadTaxDocumentRequestModel Request) : IRequest<TaxDocumentResponseModel>;

public class UploadTaxDocumentValidator : AbstractValidator<UploadTaxDocumentCommand>
{
    public const int EarliestTaxYear = 1900;

    public UploadTaxDocumentValidator(IClock clock)
    {
        RuleFor(x => x.UserId).GreaterThan(0);
        RuleFor(x => x.Request.FileAttachmentId).GreaterThan(0);
        RuleFor(x => x.Request.DocumentType).IsInEnum();
        RuleFor(x => x.Request.TaxYear)
            .InclusiveBetween(EarliestTaxYear, clock.UtcNow.Year)
            .WithMessage($"Tax year must be between {EarliestTaxYear} and {clock.UtcNow.Year}.");
    }
}

public class UploadTaxDocumentHandler(AppDbContext db)
    : IRequestHandler<UploadTaxDocumentCommand, TaxDocumentResponseModel>
{
    public async Task<TaxDocumentResponseModel> Handle(
        UploadTaxDocumentCommand request, CancellationToken ct)
    {
        var data = request.Request;

        var failures = new List<ValidationFailure>();
        if (!await db.Users.AnyAsync(u => u.Id == request.UserId, ct))
            failures.Add(new ValidationFailure(nameof(request.UserId), $"User {request.UserId} does not exist."));
        if (!await db.FileAttachments.AnyAsync(f => f.Id == data.FileAttachmentId
                && f.EntityType == "tax-documents" && f.EntityId == request.UserId, ct))
            failures.Add(new ValidationFailure(nameof(data.FileAttachmentId), $"File {data.FileAttachmentId} is not a tax document uploaded for this user."));
        if (failures.Count > 0)
            throw new ValidationException(failures);

        var doc = new TaxDocument
        {
            UserId = request.UserId,
            DocumentType = data.DocumentType,
            TaxYear = data.TaxYear,
            FileAttachmentId = data.FileAttachmentId,
            Source = PayrollDocumentSource.Manual,
        };

        db.TaxDocuments.Add(doc);
        await db.SaveChangesAsync(ct);

        return new TaxDocumentResponseModel(
            doc.Id, doc.UserId, doc.DocumentType, doc.TaxYear,
            doc.EmployerName, doc.FileAttachmentId, doc.Source, doc.ExternalId);
    }
}
