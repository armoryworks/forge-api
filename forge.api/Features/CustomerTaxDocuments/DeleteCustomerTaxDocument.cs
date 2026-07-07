using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.CustomerTaxDocuments;

public record DeleteCustomerTaxDocumentCommand(int Id) : IRequest;

public class DeleteCustomerTaxDocumentHandler(AppDbContext db, IClock clock)
    : IRequestHandler<DeleteCustomerTaxDocumentCommand>
{
    public async Task Handle(DeleteCustomerTaxDocumentCommand request, CancellationToken cancellationToken)
    {
        var document = await db.CustomerTaxDocuments
                .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Tax document {request.Id} not found");

        document.DeletedAt = clock.UtcNow;
        // DeletedBy auto-stamped by AppDbContext.SetTimestamps.

        db.LogActivityAt(
            "tax-document-removed",
            $"Removed {document.CertificateType} tax certificate ({document.StateCode})",
            ("Customer", document.CustomerId));

        await db.SaveChangesAsync(cancellationToken);
    }
}
