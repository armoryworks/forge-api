using FluentAssertions;
using MediatR;
using Moq;

using Forge.Api.Features.Quotes;
using Forge.Api.Features.SalesTax;
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

    private TaxOverrideGuard Guard(Forge.Data.Context.AppDbContext db)
        => new(db, _mediator.Object, new FixedClock(Now));

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

    // ── EnsureCanOverrideAsync ────────────────────────────────────────────────

    [Fact]
    public async Task Matching_default_rate_needs_no_certificate_and_returns_null()
    {
        using var db = TestDbContextFactory.Create();

        var result = await Guard(db).EnsureCanOverrideAsync(2, 0.0725m, 0.0725m, CancellationToken.None);

        result.Should().BeNull();
        db.AuditLogEntries.Should().BeEmpty("no override happened, so nothing to audit");
    }

    [Fact]
    public async Task Deviation_without_verified_document_throws()
    {
        using var db = TestDbContextFactory.Create();
        db.CustomerTaxDocuments.Add(Document(status: TaxDocumentStatus.Pending));
        await db.SaveChangesAsync();

        var act = () => Guard(db).EnsureCanOverrideAsync(2, 0m, 0.0725m, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(
            "Editing the tax rate requires a verified state tax certificate on file for this customer.");
    }

    [Fact]
    public async Task Deviation_with_verified_document_returns_document_id_and_audits()
    {
        using var db = TestDbContextFactory.Create();
        db.CustomerTaxDocuments.Add(Document(expires: Now.AddYears(1)));
        await db.SaveChangesAsync();
        // Set the actor AFTER seeding so the seed's auto-captured audit rows
        // (CaptureAuditEntries skips null-user operations) don't pollute the assert.
        db.CurrentUserId = 7;

        var result = await Guard(db).EnsureCanOverrideAsync(2, 0m, 0.0725m, CancellationToken.None);

        result.Should().Be(5);

        await db.SaveChangesAsync();
        var audit = db.AuditLogEntries.Single(a => a.Action == "quote.tax_override");
        audit.UserId.Should().Be(7);
        audit.EntityType.Should().Be("Customer");
        audit.EntityId.Should().Be(2);
        audit.Details.Should().Contain("\"taxDocumentId\":5").And.Contain("\"newRate\":0");
    }

    [Fact]
    public async Task Expired_verified_document_does_not_qualify()
    {
        using var db = TestDbContextFactory.Create();
        db.CustomerTaxDocuments.Add(Document(expires: Now.AddDays(-1)));
        await db.SaveChangesAsync();

        var act = () => Guard(db).EnsureCanOverrideAsync(2, 0m, 0.0725m, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Rejected_document_does_not_qualify()
    {
        using var db = TestDbContextFactory.Create();
        db.CustomerTaxDocuments.Add(Document(status: TaxDocumentStatus.Rejected));
        await db.SaveChangesAsync();

        var act = () => Guard(db).EnsureCanOverrideAsync(2, 0m, 0.0725m, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── Wiring into CreateQuoteHandler ────────────────────────────────────────

    [Fact]
    public async Task CreateQuote_with_override_and_verified_document_stamps_TaxDocumentId()
    {
        using var db = TestDbContextFactory.Create();
        db.CustomerTaxDocuments.Add(Document(expires: Now.AddYears(1)));
        await db.SaveChangesAsync();
        SetupDefaultRate(0.0725m);

        Quote? added = null;
        var handler = QuoteHandler(db, q => added = q);

        await handler.Handle(Command(taxRate: 0m), CancellationToken.None);

        added.Should().NotBeNull();
        added!.TaxDocumentId.Should().Be(5);
    }

    [Fact]
    public async Task CreateQuote_matching_default_rate_leaves_TaxDocumentId_null()
    {
        using var db = TestDbContextFactory.Create();
        SetupDefaultRate(0.0725m);

        Quote? added = null;
        var handler = QuoteHandler(db, q => added = q);

        await handler.Handle(Command(taxRate: 0.0725m), CancellationToken.None);

        added.Should().NotBeNull();
        added!.TaxDocumentId.Should().BeNull();
    }

    [Fact]
    public async Task CreateQuote_with_override_but_no_certificate_throws()
    {
        using var db = TestDbContextFactory.Create();
        SetupDefaultRate(0.0725m);

        var handler = QuoteHandler(db, _ => { });

        var act = () => handler.Handle(Command(taxRate: 0m), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*verified state tax certificate*");
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

    private static CreateQuoteCommand Command(decimal taxRate) =>
        new(2, null, null, null, taxRate, [new CreateQuoteLineModel(null, "Widget", 1, 10m, null)]);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
