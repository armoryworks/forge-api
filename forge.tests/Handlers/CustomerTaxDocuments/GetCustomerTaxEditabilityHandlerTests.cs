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

    private static GetCustomerTaxEditabilityHandler Handler(Forge.Data.Context.AppDbContext db, string role = "OfficeManager")
        => new(db, new FixedClock(Now), TestCaller.As(7, role));

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

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    [InlineData("OfficeManager")]
    [InlineData("Controller")]
    public async Task Tax_managing_roles_may_edit_the_rate_without_a_certificate(string role)
    {
        using var db = TestDbContextFactory.Create();

        var result = await Handler(db, role).Handle(new GetCustomerTaxEditabilityQuery(2), CancellationToken.None);

        result.CanEditTax.Should().BeTrue();
        result.Reason.Should().BeNull();
        result.ActiveDocumentId.Should().BeNull();
    }

    [Theory]
    [InlineData("PM")]
    [InlineData("Engineer")]
    public async Task Other_roles_are_locked_even_with_a_certificate_on_file(string role)
    {
        using var db = TestDbContextFactory.Create();
        db.SalesTaxRates.Add(DefaultRate());
        db.CustomerTaxDocuments.Add(Document(TaxDocumentStatus.Verified, expires: Now.AddYears(1)));
        await db.SaveChangesAsync();

        var result = await Handler(db, role).Handle(new GetCustomerTaxEditabilityQuery(2), CancellationToken.None);

        result.CanEditTax.Should().BeFalse();
        result.RateDetermined.Should().BeTrue();
        result.Reason.Should().Contain("Controller");
    }

    [Theory]
    [InlineData("PM")]
    [InlineData("Engineer")]
    public async Task With_no_tax_configured_every_role_enters_the_rate(string role)
    {
        using var db = TestDbContextFactory.Create();
        db.Customers.Add(new Customer { Id = 2, Name = "Acme Corp" });
        await db.SaveChangesAsync();

        var result = await Handler(db, role).Handle(new GetCustomerTaxEditabilityQuery(2), CancellationToken.None);

        result.RateDetermined.Should().BeFalse();
        result.CanEditTax.Should().BeTrue();
        result.Reason.Should().BeNull();
    }

    [Fact]
    public async Task An_exempt_customer_has_a_determined_rate_even_with_no_tax_configured()
    {
        using var db = TestDbContextFactory.Create();
        db.Customers.Add(new Customer { Id = 2, Name = "Acme Corp", IsTaxExempt = true });
        await db.SaveChangesAsync();

        var result = await Handler(db, "PM").Handle(new GetCustomerTaxEditabilityQuery(2), CancellationToken.None);

        result.RateDetermined.Should().BeTrue();
        result.CanEditTax.Should().BeFalse();
    }

    private static SalesTaxRate DefaultRate() => new()
    {
        Name = "Default", Code = "DEF", Rate = 0.0725m, EffectiveFrom = Now.AddYears(-1), IsDefault = true, IsActive = true,
    };

    [Fact]
    public async Task A_verified_unexpired_certificate_is_reported()
    {
        using var db = TestDbContextFactory.Create();
        var doc = Document(TaxDocumentStatus.Verified, expires: Now.AddYears(1));
        db.CustomerTaxDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetCustomerTaxEditabilityQuery(2), CancellationToken.None);

        result.ActiveDocumentId.Should().Be(doc.Id);
        result.StateCode.Should().Be("CA");
        result.ExpiresAt.Should().Be(doc.ExpirationDate);
    }

    [Fact]
    public async Task Expired_pending_or_rejected_certificates_are_not_reported()
    {
        using var db = TestDbContextFactory.Create();
        db.CustomerTaxDocuments.Add(Document(TaxDocumentStatus.Verified, expires: Now.AddDays(-1)));
        db.CustomerTaxDocuments.Add(Document(TaxDocumentStatus.Pending));
        db.CustomerTaxDocuments.Add(Document(TaxDocumentStatus.Rejected));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetCustomerTaxEditabilityQuery(2), CancellationToken.None);

        result.ActiveDocumentId.Should().BeNull();
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
