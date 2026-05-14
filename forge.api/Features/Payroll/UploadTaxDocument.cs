using MediatR;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Payroll;

public record UploadTaxDocumentCommand(int UserId, UploadTaxDocumentRequestModel Request) : IRequest<TaxDocumentResponseModel>;

public class UploadTaxDocumentHandler(AppDbContext db)
    : IRequestHandler<UploadTaxDocumentCommand, TaxDocumentResponseModel>
{
    public async Task<TaxDocumentResponseModel> Handle(
        UploadTaxDocumentCommand request, CancellationToken ct)
    {
        var data = request.Request;

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
