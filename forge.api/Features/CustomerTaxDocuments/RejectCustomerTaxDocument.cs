using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.CustomerTaxDocuments;

public record RejectCustomerTaxDocumentCommand(int Id, string Reason) : IRequest;

public class RejectCustomerTaxDocumentValidator : AbstractValidator<RejectCustomerTaxDocumentCommand>
{
    public RejectCustomerTaxDocumentValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A rejection reason is required.")
            .MaximumLength(500);
    }
}

public class RejectCustomerTaxDocumentHandler(AppDbContext db)
    : IRequestHandler<RejectCustomerTaxDocumentCommand>
{
    public async Task Handle(RejectCustomerTaxDocumentCommand request, CancellationToken cancellationToken)
    {
        var document = await db.CustomerTaxDocuments
                .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Tax document {request.Id} not found");

        document.Status = TaxDocumentStatus.Rejected;
        document.RejectionReason = request.Reason.Trim();
        // A rejected document has no verifier — clear any earlier verification stamp.
        document.VerifiedById = null;
        document.VerifiedAt = null;

        db.LogActivityAt(
            "tax-document-rejected",
            $"Rejected {document.CertificateType} tax certificate ({document.StateCode}): {document.RejectionReason}",
            ("Customer", document.CustomerId));

        await db.SaveChangesAsync(cancellationToken);
    }
}
