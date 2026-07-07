using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.CustomerTaxDocuments;

public record CreateCustomerTaxDocumentCommand(
    int CustomerId,
    int FileAttachmentId,
    string StateCode,
    string CertificateType,
    string? CertificateNumber,
    DateTimeOffset? ExpirationDate) : IRequest<CustomerTaxDocumentResponseModel>;

public class CreateCustomerTaxDocumentValidator : AbstractValidator<CreateCustomerTaxDocumentCommand>
{
    private static readonly string[] CertificateTypes = ["Resale", "Exemption", "DirectPay", "Other"];

    public CreateCustomerTaxDocumentValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.FileAttachmentId).GreaterThan(0);
        RuleFor(x => x.StateCode).NotEmpty().Length(2)
            .WithMessage("State code must be a 2-character state abbreviation.");
        RuleFor(x => x.CertificateType)
            .Must(t => CertificateTypes.Contains(t))
            .WithMessage("Certificate type must be one of: Resale, Exemption, DirectPay, Other.");
        RuleFor(x => x.CertificateNumber).MaximumLength(100);
    }
}

public class CreateCustomerTaxDocumentHandler(AppDbContext db)
    : IRequestHandler<CreateCustomerTaxDocumentCommand, CustomerTaxDocumentResponseModel>
{
    public async Task<CustomerTaxDocumentResponseModel> Handle(
        CreateCustomerTaxDocumentCommand request, CancellationToken cancellationToken)
    {
        _ = await db.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken)
            ?? throw new KeyNotFoundException($"Customer {request.CustomerId} not found");

        // Link an EXISTING upload — the file must already be attached to this customer.
        var file = await db.FileAttachments.AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == request.FileAttachmentId, cancellationToken)
            ?? throw new KeyNotFoundException($"File attachment {request.FileAttachmentId} not found");

        if (file.EntityType != "customers" || file.EntityId != request.CustomerId)
            throw new InvalidOperationException(
                "The file attachment must belong to this customer (entityType \"customers\").");

        var document = new CustomerTaxDocument
        {
            CustomerId = request.CustomerId,
            FileAttachmentId = request.FileAttachmentId,
            StateCode = request.StateCode.Trim().ToUpperInvariant(),
            CertificateType = request.CertificateType,
            CertificateNumber = request.CertificateNumber,
            ExpirationDate = request.ExpirationDate,
            Status = TaxDocumentStatus.Pending,
        };

        db.CustomerTaxDocuments.Add(document);

        // Definitional master-data change on the Customer — tax exemption state
        // gates downstream quote pricing, so it logs on the customer.
        db.LogActivityAt(
            "tax-document-added",
            $"Added {document.CertificateType} tax certificate ({document.StateCode}) — pending verification",
            ("Customer", request.CustomerId));

        await db.SaveChangesAsync(cancellationToken);

        return new CustomerTaxDocumentResponseModel(
            document.Id, document.FileAttachmentId, file.FileName,
            document.StateCode, document.CertificateType, document.CertificateNumber,
            document.Status.ToString(), document.VerifiedAt, null,
            document.ExpirationDate, document.RejectionReason);
    }
}
