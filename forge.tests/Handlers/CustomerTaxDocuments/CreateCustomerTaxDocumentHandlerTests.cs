using FluentAssertions;

using Forge.Api.Features.CustomerTaxDocuments;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.CustomerTaxDocuments;

public class CreateCustomerTaxDocumentHandlerTests
{
    private static Customer Customer(int id = 1) => new() { Id = id, Name = "Acme Corp" };

    private static FileAttachment CustomerFile(int id = 10, int customerId = 1) => new()
    {
        Id = id,
        FileName = "resale-cert.pdf",
        ContentType = "application/pdf",
        EntityType = "customers",
        EntityId = customerId,
        UploadedById = 7,
    };

    private static CreateCustomerTaxDocumentCommand Command(int customerId = 1, int fileId = 10) =>
        new(customerId, fileId, "ca", "Resale", "CERT-123", DateTimeOffset.UtcNow.AddYears(1));

    [Fact]
    public async Task Creates_pending_document_and_logs_activity_on_customer()
    {
        using var db = TestDbContextFactory.Create();
        db.CurrentUserId = 7;
        db.Customers.Add(Customer());
        db.FileAttachments.Add(CustomerFile());
        await db.SaveChangesAsync();

        var result = await new CreateCustomerTaxDocumentHandler(db)
            .Handle(Command(), CancellationToken.None);

        result.Status.Should().Be(nameof(TaxDocumentStatus.Pending));
        result.StateCode.Should().Be("CA", "state code is normalized to uppercase");
        result.FileName.Should().Be("resale-cert.pdf");
        result.CertificateType.Should().Be("Resale");

        var saved = db.CustomerTaxDocuments.Single();
        saved.Status.Should().Be(TaxDocumentStatus.Pending);
        saved.CustomerId.Should().Be(1);
        saved.FileAttachmentId.Should().Be(10);

        // Filtered by action: AppDbContext.CaptureAuditEntries also writes
        // automatic "Created" rows for every added entity.
        var activity = db.ActivityLogs.Single(a => a.Action == "tax-document-added");
        activity.EntityType.Should().Be("Customer");
        activity.EntityId.Should().Be(1);
    }

    [Fact]
    public async Task Rejects_a_file_attached_to_a_different_customer()
    {
        using var db = TestDbContextFactory.Create();
        db.Customers.Add(Customer());
        db.FileAttachments.Add(CustomerFile(customerId: 99));
        await db.SaveChangesAsync();

        var act = () => new CreateCustomerTaxDocumentHandler(db)
            .Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*must belong to this customer*");
    }

    [Fact]
    public async Task Rejects_a_file_attached_to_a_non_customer_entity()
    {
        using var db = TestDbContextFactory.Create();
        db.Customers.Add(Customer());
        var file = CustomerFile();
        file.EntityType = "jobs";
        db.FileAttachments.Add(file);
        await db.SaveChangesAsync();

        var act = () => new CreateCustomerTaxDocumentHandler(db)
            .Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Throws_when_file_attachment_does_not_exist()
    {
        using var db = TestDbContextFactory.Create();
        db.Customers.Add(Customer());
        await db.SaveChangesAsync();

        var act = () => new CreateCustomerTaxDocumentHandler(db)
            .Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*File attachment 10*");
    }

    [Fact]
    public void Validator_requires_two_char_state_and_known_certificate_type()
    {
        var validator = new CreateCustomerTaxDocumentValidator();

        validator.Validate(Command()).IsValid.Should().BeTrue();
        validator.Validate(Command() with { StateCode = "CAL" }).IsValid.Should().BeFalse();
        validator.Validate(Command() with { StateCode = "" }).IsValid.Should().BeFalse();
        validator.Validate(Command() with { CertificateType = "Bogus" }).IsValid.Should().BeFalse();
    }
}
