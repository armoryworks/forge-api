using FluentAssertions;

using Forge.Api.Features.CustomerTaxDocuments;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.CustomerTaxDocuments;

public class RejectCustomerTaxDocumentHandlerTests
{
    private static CustomerTaxDocument VerifiedDocument() => new()
    {
        Id = 5,
        CustomerId = 2,
        FileAttachmentId = 10,
        StateCode = "CA",
        CertificateType = "Exemption",
        Status = TaxDocumentStatus.Verified,
        VerifiedById = 7,
        VerifiedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Rejects_document_with_reason_and_clears_verification()
    {
        using var db = TestDbContextFactory.Create();
        var doc = VerifiedDocument();
        db.CustomerTaxDocuments.Add(doc);
        await db.SaveChangesAsync();

        await new RejectCustomerTaxDocumentHandler(db)
            .Handle(new RejectCustomerTaxDocumentCommand(5, "  Certificate is illegible  "), CancellationToken.None);

        doc.Status.Should().Be(TaxDocumentStatus.Rejected);
        doc.RejectionReason.Should().Be("Certificate is illegible");
        doc.VerifiedById.Should().BeNull();
        doc.VerifiedAt.Should().BeNull();

        // Filtered by action: CaptureAuditEntries also writes automatic
        // FieldChanged rows for the modified entity.
        var activity = db.ActivityLogs.Single(a => a.Action == "tax-document-rejected");
        activity.EntityType.Should().Be("Customer");
        activity.EntityId.Should().Be(2);
    }

    [Fact]
    public async Task Throws_when_document_not_found()
    {
        using var db = TestDbContextFactory.Create();

        var act = () => new RejectCustomerTaxDocumentHandler(db)
            .Handle(new RejectCustomerTaxDocumentCommand(999, "reason"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public void Validator_requires_a_reason()
    {
        var validator = new RejectCustomerTaxDocumentValidator();

        validator.Validate(new RejectCustomerTaxDocumentCommand(5, "too old")).IsValid.Should().BeTrue();
        validator.Validate(new RejectCustomerTaxDocumentCommand(5, "")).IsValid.Should().BeFalse();
        validator.Validate(new RejectCustomerTaxDocumentCommand(5, new string('x', 501))).IsValid.Should().BeFalse();
    }
}
