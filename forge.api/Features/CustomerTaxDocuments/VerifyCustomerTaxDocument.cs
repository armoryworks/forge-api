using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.CustomerTaxDocuments;

public record VerifyCustomerTaxDocumentCommand(int Id) : IRequest;

public class VerifyCustomerTaxDocumentHandler(
    AppDbContext db,
    IHttpContextAccessor httpContext,
    IClock clock) : IRequestHandler<VerifyCustomerTaxDocumentCommand>
{
    public async Task Handle(VerifyCustomerTaxDocumentCommand request, CancellationToken cancellationToken)
    {
        var document = await db.CustomerTaxDocuments
                .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Tax document {request.Id} not found");

        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        document.Status = TaxDocumentStatus.Verified;
        document.VerifiedById = userId;
        document.VerifiedAt = clock.UtcNow;
        document.RejectionReason = null;

        db.LogActivityAt(
            "tax-document-verified",
            $"Verified {document.CertificateType} tax certificate ({document.StateCode})",
            ("Customer", document.CustomerId));

        await db.SaveChangesAsync(cancellationToken);
    }
}
