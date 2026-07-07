using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CustomerTaxDocuments;

public record GetCustomerTaxDocumentsQuery(int CustomerId) : IRequest<List<CustomerTaxDocumentResponseModel>>;

public class GetCustomerTaxDocumentsHandler(AppDbContext db)
    : IRequestHandler<GetCustomerTaxDocumentsQuery, List<CustomerTaxDocumentResponseModel>>
{
    public async Task<List<CustomerTaxDocumentResponseModel>> Handle(
        GetCustomerTaxDocumentsQuery request, CancellationToken cancellationToken)
    {
        // Left-join to Users so a document whose verifier was removed still lists.
        return await (
            from d in db.CustomerTaxDocuments.AsNoTracking()
            where d.CustomerId == request.CustomerId
            join f in db.FileAttachments.AsNoTracking() on d.FileAttachmentId equals f.Id
            join u in db.Users.AsNoTracking() on d.VerifiedById equals (int?)u.Id into uj
            from u in uj.DefaultIfEmpty()
            orderby d.CreatedAt descending
            select new CustomerTaxDocumentResponseModel(
                d.Id,
                d.FileAttachmentId,
                f.FileName,
                d.StateCode,
                d.CertificateType,
                d.CertificateNumber,
                d.Status.ToString(),
                d.VerifiedAt,
                u != null ? u.LastName + ", " + u.FirstName : null,
                d.ExpirationDate,
                d.RejectionReason)
        ).ToListAsync(cancellationToken);
    }
}
