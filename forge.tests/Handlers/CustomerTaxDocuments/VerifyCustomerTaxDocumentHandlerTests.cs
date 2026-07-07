using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;

using Forge.Api.Features.CustomerTaxDocuments;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.CustomerTaxDocuments;

public class VerifyCustomerTaxDocumentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IHttpContextAccessor> _httpContextAccessor = new();

    public VerifyCustomerTaxDocumentHandlerTests()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "7")], "Test"));
        _httpContextAccessor.Setup(a => a.HttpContext)
            .Returns(new DefaultHttpContext { User = principal });
    }

    private VerifyCustomerTaxDocumentHandler Handler(Forge.Data.Context.AppDbContext db)
        => new(db, _httpContextAccessor.Object, new FixedClock(Now));

    private static CustomerTaxDocument Document(TaxDocumentStatus status = TaxDocumentStatus.Pending) => new()
    {
        Id = 5,
        CustomerId = 2,
        FileAttachmentId = 10,
        StateCode = "CA",
        CertificateType = "Resale",
        Status = status,
    };

    [Fact]
    public async Task Verifies_document_and_stamps_verifier()
    {
        using var db = TestDbContextFactory.Create();
        var doc = Document();
        doc.RejectionReason = "was blurry";
        db.CustomerTaxDocuments.Add(doc);
        await db.SaveChangesAsync();

        await Handler(db).Handle(new VerifyCustomerTaxDocumentCommand(5), CancellationToken.None);

        doc.Status.Should().Be(TaxDocumentStatus.Verified);
        doc.VerifiedById.Should().Be(7);
        doc.VerifiedAt.Should().Be(Now);
        doc.RejectionReason.Should().BeNull("verify must clear any earlier rejection reason");

        // Filtered by action: CaptureAuditEntries also writes automatic
        // FieldChanged rows for the modified entity.
        var activity = db.ActivityLogs.Single(a => a.Action == "tax-document-verified");
        activity.EntityType.Should().Be("Customer");
        activity.EntityId.Should().Be(2);
    }

    [Fact]
    public async Task Throws_when_document_not_found()
    {
        using var db = TestDbContextFactory.Create();

        var act = () => Handler(db).Handle(new VerifyCustomerTaxDocumentCommand(999), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
