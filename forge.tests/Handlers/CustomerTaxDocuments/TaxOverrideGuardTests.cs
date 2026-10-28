using FluentAssertions;
using FluentValidation;
using MediatR;
using Moq;

using Forge.Api.Features.Quotes;
using Forge.Api.Features.SalesTax;
using Forge.Api.Middleware;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.CustomerTaxDocuments;

public class TaxOverrideGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IMediator> _mediator = new();

    private TaxOverrideGuard Guard(Forge.Data.Context.AppDbContext db, params string[] roles)
        => new(db, _mediator.Object, new FixedClock(Now), TestCaller.As(7, roles.Length == 0 ? ["Controller"] : roles));

    private static CustomerTaxDocument Document(
        TaxDocumentStatus status = TaxDocumentStatus.Verified,
        DateTimeOffset? expires = null) => new()
    {
        Id = 5,
        CustomerId = 2,
        FileAttachmentId = 10,
        StateCode = "CA",
        CertificateType = "Resale",
        Status = status,
        VerifiedById = 7,
        VerifiedAt = Now.AddDays(-1),
        ExpirationDate = expires,
    };

    private static async Task<string> FailureCode(Func<Task> act)
        => (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Single().ErrorCode;

    [Fact]
    public async Task Matching_default_rate_needs_no_reason_and_returns_null()
    {
        using var db = TestDbContextFactory.Create();

        var result = await Guard(db, "PM").EnsureCanOverrideAsync(2, 0.0725m, 0.0725m, null, null, "invoice", CancellationToken.None);

        result.Should().BeNull();
        db.AuditLogEntries.Should().BeEmpty("no override happened, so nothing to audit");
    }

    [Theory]
    [InlineData("PM")]
    [InlineData("Engineer")]
    [InlineData("Procurement")]
    public async Task A_role_outside_tax_management_cannot_override(string role)
    {
        using var db = TestDbContextFactory.Create();

        var act = () => Guard(db, role).EnsureCanOverrideAsync(
            2, 0m, 0.0725m, TaxOverrideReason.OutOfJurisdiction, null, "invoice", CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>()).Which.Code.Should().Be(TaxOverrideGuard.ForbiddenCode);
    }

    [Fact]
    public async Task An_override_without_a_reason_is_refused()
    {
        using var db = TestDbContextFactory.Create();

        var code = await FailureCode(() => Guard(db).EnsureCanOverrideAsync(2, 0m, 0.0725m, null, null, "invoice", CancellationToken.None));

        code.Should().Be(TaxOverrideGuard.ReasonRequiredCode);
    }

    [Fact]
    public async Task Other_needs_a_note()
    {
        using var db = TestDbContextFactory.Create();

        var code = await FailureCode(() => Guard(db).EnsureCanOverrideAsync(
            2, 0m, 0.0725m, TaxOverrideReason.Other, "  ", "invoice", CancellationToken.None));

        code.Should().Be(TaxOverrideGuard.NoteRequiredCode);
    }

    [Theory]
    [InlineData(TaxOverrideReason.Resale)]
    [InlineData(TaxOverrideReason.ExemptOrganization)]
    [InlineData(TaxOverrideReason.Government)]
    public async Task A_certificate_reason_needs_a_verified_certificate(TaxOverrideReason reason)
    {
        using var db = TestDbContextFactory.Create();
        db.CustomerTaxDocuments.Add(Document(status: TaxDocumentStatus.Pending));
        await db.SaveChangesAsync();

        var code = await FailureCode(() => Guard(db).EnsureCanOverrideAsync(2, 0m, 0.0725m, reason, null, "invoice", CancellationToken.None));

        code.Should().Be(TaxOverrideGuard.CertificateRequiredCode);
    }

    [Fact]
    public async Task Expired_or_rejected_certificates_do_not_qualify()
    {
        using var db = TestDbContextFactory.Create();
        db.CustomerTaxDocuments.Add(Document(expires: Now.AddDays(-1)));
        await db.SaveChangesAsync();

        var code = await FailureCode(() => Guard(db).EnsureCanOverrideAsync(
            2, 0m, 0.0725m, TaxOverrideReason.Resale, null, "invoice", CancellationToken.None));

        code.Should().Be(TaxOverrideGuard.CertificateRequiredCode);
    }

    [Fact]
    public async Task Out_of_jurisdiction_needs_no_certificate_and_is_audited_with_its_reason()
    {
        using var db = TestDbContextFactory.Create();
        db.CurrentUserId = 7;

        var stamp = await Guard(db, "OfficeManager").EnsureCanOverrideAsync(
            2, 0m, 0.0725m, TaxOverrideReason.OutOfJurisdiction, " shipped to Oregon ", "invoice", CancellationToken.None);

        stamp.Should().Be(new TaxOverrideStamp(TaxOverrideReason.OutOfJurisdiction, "shipped to Oregon", null));
        await db.SaveChangesAsync();
        var audit = db.AuditLogEntries.Single(a => a.Action == "invoice.tax_override");
        audit.UserId.Should().Be(7);
        audit.EntityType.Should().Be("Customer");
        audit.EntityId.Should().Be(2);
        audit.Details.Should().Contain("\"reason\":\"OutOfJurisdiction\"").And.Contain("\"newRate\":0").And.Contain("shipped to Oregon");
    }

    [Fact]
    public async Task A_certificate_reason_with_a_verified_certificate_stamps_the_certificate()
    {
        using var db = TestDbContextFactory.Create();
        db.CustomerTaxDocuments.Add(Document(expires: Now.AddYears(1)));
        await db.SaveChangesAsync();

        var stamp = await Guard(db, "Admin").EnsureCanOverrideAsync(
            2, 0m, 0.0725m, TaxOverrideReason.Resale, null, "quote", CancellationToken.None);

        stamp!.TaxDocumentId.Should().Be(5);
        stamp.Reason.Should().Be(TaxOverrideReason.Resale);
    }

    [Fact]
    public void Copying_an_override_needs_the_role_but_not_the_reason_again()
    {
        using var db = TestDbContextFactory.Create();

        var copy = Guard(db).EnsureCanCopyOverride(2, 0m, 0.0725m, TaxOverrideReason.Resale, "cert R-1", 5, "quote");
        var denied = () => Guard(db, "PM").EnsureCanCopyOverride(2, 0m, 0.0725m, TaxOverrideReason.Resale, "cert R-1", 5, "quote");

        copy.Should().Be(new TaxOverrideStamp(TaxOverrideReason.Resale, "cert R-1", 5));
        denied.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void The_override_roles_are_the_customer_tax_roles_plus_the_controller()
    {
        TaxOverrideGuard.OverrideRoles.Should().BeEquivalentTo(
            Forge.Api.Controllers.CustomersController.CommercialTermsRoles.Append("Controller"));
    }

    [Fact]
    public async Task CreateQuote_with_a_certificate_reason_stamps_the_quote()
    {
        using var db = TestDbContextFactory.Create();
        db.CustomerTaxDocuments.Add(Document(expires: Now.AddYears(1)));
        await db.SaveChangesAsync();
        SetupDefaultRate(0.0725m);

        Quote? added = null;
        var handler = QuoteHandler(db, q => added = q);

        await handler.Handle(Command(taxRate: 0m, TaxOverrideReason.Resale), CancellationToken.None);

        added!.TaxDocumentId.Should().Be(5);
        added.TaxOverrideReason.Should().Be(TaxOverrideReason.Resale);
    }

    [Fact]
    public async Task CreateQuote_matching_default_rate_leaves_the_override_empty()
    {
        using var db = TestDbContextFactory.Create();
        SetupDefaultRate(0.0725m);

        Quote? added = null;
        var handler = QuoteHandler(db, q => added = q);

        await handler.Handle(Command(taxRate: 0.0725m), CancellationToken.None);

        added!.TaxDocumentId.Should().BeNull();
        added.TaxOverrideReason.Should().BeNull();
    }

    [Fact]
    public async Task CreateQuote_with_an_override_but_no_reason_is_refused()
    {
        using var db = TestDbContextFactory.Create();
        SetupDefaultRate(0.0725m);

        var handler = QuoteHandler(db, _ => { });

        var code = await FailureCode(() => handler.Handle(Command(taxRate: 0m), CancellationToken.None));

        code.Should().Be(TaxOverrideGuard.ReasonRequiredCode);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private void SetupDefaultRate(decimal rate)
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<GetTaxRateForCustomerQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesTaxRateResponseModel(
                1, "CA State", "CA", "CA", rate, Now.AddYears(-1), null, false, true, null));
    }

    private CreateQuoteHandler QuoteHandler(Forge.Data.Context.AppDbContext db, Action<Quote> onAdd)
    {
        var quoteRepo = new Mock<IQuoteRepository>();
        quoteRepo.Setup(r => r.GenerateNextQuoteNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("QUO-0001");
        quoteRepo.Setup(r => r.AddAsync(It.IsAny<Quote>(), It.IsAny<CancellationToken>()))
            .Callback<Quote, CancellationToken>((q, _) => onAdd(q))
            .Returns(Task.CompletedTask);

        var customerRepo = new Mock<ICustomerRepository>();
        customerRepo.Setup(r => r.FindAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer { Id = 2, Name = "Acme Corp" });

        var partRepo = new Mock<IPartRepository>();

        return new CreateQuoteHandler(
            quoteRepo.Object, customerRepo.Object, partRepo.Object, null, Guard(db));
    }

    private static CreateQuoteCommand Command(decimal taxRate, TaxOverrideReason? reason = null) =>
        new(2, null, null, null, taxRate, [new CreateQuoteLineModel(null, "Widget", 1, 10m, null)], TaxOverrideReason: reason);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
