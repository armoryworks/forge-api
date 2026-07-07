using FluentAssertions;

using Forge.Api.Features.CustomerTaxDocuments;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.CustomerTaxDocuments;

public class GetCustomerTaxEditabilityHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);

    private static GetCustomerTaxEditabilityHandler Handler(Forge.Data.Context.AppDbContext db)
        => new(db, new FixedClock(Now));

    private static CustomerTaxDocument Document(
        TaxDocumentStatus status, DateTimeOffset? expires = null) => new()
    {
        CustomerId = 2,
        FileAttachmentId = 10,
        StateCode = "CA",
        CertificateType = "Resale",
        Status = status,
        VerifiedAt = status == TaxDocumentStatus.Verified ? Now.AddDays(-1) : null,
        ExpirationDate = expires,
    };

    [Fact]
    public async Task Verified_unexpired_document_unlocks_editing()
    {
        using var db = TestDbContextFactory.Create();
        var doc = Document(TaxDocumentStatus.Verified, expires: Now.AddYears(1));
        db.CustomerTaxDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetCustomerTaxEditabilityQuery(2), CancellationToken.None);

        result.CanEditTax.Should().BeTrue();
        result.Reason.Should().BeNull();
        result.ActiveDocumentId.Should().Be(doc.Id);
        result.StateCode.Should().Be("CA");
        result.ExpiresAt.Should().Be(doc.ExpirationDate);
    }

    [Fact]
    public async Task No_documents_locks_editing_with_reason()
    {
        using var db = TestDbContextFactory.Create();

        var result = await Handler(db).Handle(new GetCustomerTaxEditabilityQuery(2), CancellationToken.None);

        result.CanEditTax.Should().BeFalse();
        result.Reason.Should().Contain("No verified state tax certificate");
        result.ActiveDocumentId.Should().BeNull();
    }

    [Fact]
    public async Task Expired_verified_document_locks_editing_with_expired_reason()
    {
        using var db = TestDbContextFactory.Create();
        db.CustomerTaxDocuments.Add(Document(TaxDocumentStatus.Verified, expires: Now.AddDays(-1)));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetCustomerTaxEditabilityQuery(2), CancellationToken.None);

        result.CanEditTax.Should().BeFalse();
        result.Reason.Should().Contain("expired");
    }

    [Fact]
    public async Task Pending_or_rejected_documents_do_not_unlock_editing()
    {
        using var db = TestDbContextFactory.Create();
        db.CustomerTaxDocuments.Add(Document(TaxDocumentStatus.Pending));
        db.CustomerTaxDocuments.Add(Document(TaxDocumentStatus.Rejected));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetCustomerTaxEditabilityQuery(2), CancellationToken.None);

        result.CanEditTax.Should().BeFalse();
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
