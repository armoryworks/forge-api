using FluentAssertions;

using Forge.Api.Features.Terms;
using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Terms;

public class TermsDocumentCrudHandlerTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly IClock _clock = new SystemClock();

    private static readonly DateTimeOffset EffectiveFrom = new(2026, 01, 01, 0, 0, 0, TimeSpan.Zero);

    private static CreateTermsDocumentRequestModel ValidBody(
        TermsScope scope = TermsScope.Company,
        int? customerId = null,
        int? partId = null,
        string title = "Standard Terms") => new(
        Scope: scope,
        CustomerId: customerId,
        PartId: partId,
        Title: title,
        Summary: null,
        BodyMarkdown: "All sales are final.",
        EffectiveFrom: EffectiveFrom,
        EffectiveTo: null);

    // ── Validator: scope ↔ FK consistency ────────────────────────────────

    [Theory]
    [InlineData(TermsScope.Company, null, null, true)]
    [InlineData(TermsScope.Company, 1, null, false)]     // company forbids CustomerId
    [InlineData(TermsScope.Company, null, 1, false)]     // company forbids PartId
    [InlineData(TermsScope.Customer, 1, null, true)]
    [InlineData(TermsScope.Customer, null, null, false)] // customer requires CustomerId
    [InlineData(TermsScope.Customer, 1, 1, false)]       // customer forbids PartId
    [InlineData(TermsScope.Part, null, 1, true)]
    [InlineData(TermsScope.Part, null, null, false)]     // part requires PartId
    [InlineData(TermsScope.Part, 1, 1, false)]           // part forbids CustomerId
    public void CreateValidator_EnforcesScopeFkConsistency(
        TermsScope scope, int? customerId, int? partId, bool expectedValid)
    {
        var validator = new CreateTermsDocumentValidator();
        var command = new CreateTermsDocumentCommand(
            ValidBody(scope, customerId, partId), CallerIsAdmin: true);

        validator.Validate(command).IsValid.Should().Be(expectedValid);
    }

    [Fact]
    public void CreateValidator_RejectsMissingEffectiveFrom_AndOversizedFields()
    {
        var validator = new CreateTermsDocumentValidator();

        var noEffectiveFrom = ValidBody() with { EffectiveFrom = default };
        validator.Validate(new CreateTermsDocumentCommand(noEffectiveFrom, true))
            .IsValid.Should().BeFalse();

        var longTitle = ValidBody() with { Title = new string('t', 301) };
        validator.Validate(new CreateTermsDocumentCommand(longTitle, true))
            .IsValid.Should().BeFalse();

        var longSummary = ValidBody() with { Summary = new string('s', 1001) };
        validator.Validate(new CreateTermsDocumentCommand(longSummary, true))
            .IsValid.Should().BeFalse();
    }

    // ── Create handler ────────────────────────────────────────────────────

    [Fact]
    public async Task Create_CustomerScope_PersistsAndLogsActivityOnCustomer()
    {
        var customer = new Customer { Name = "Acme" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var handler = new CreateTermsDocumentHandler(_db);
        var result = await handler.Handle(new CreateTermsDocumentCommand(
            ValidBody(TermsScope.Customer, customerId: customer.Id), CallerIsAdmin: false),
            CancellationToken.None);

        result.Version.Should().Be(1);
        result.Scope.Should().Be("Customer");

        // Note: AppDbContext auto-captures generic "created" rows; assert on
        // the handler's explicit indexing-point row.
        var log = _db.ActivityLogs.Single(l => l.Action == "terms-document-added");
        log.EntityType.Should().Be("Customer");
        log.EntityId.Should().Be(customer.Id);
    }

    [Fact]
    public async Task Create_CompanyScope_NonAdmin_Throws()
    {
        var handler = new CreateTermsDocumentHandler(_db);
        var act = () => handler.Handle(
            new CreateTermsDocumentCommand(ValidBody(), CallerIsAdmin: false),
            CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Create_CompanyScope_Admin_Succeeds_WithNoActivityAnchor()
    {
        var handler = new CreateTermsDocumentHandler(_db);
        await handler.Handle(
            new CreateTermsDocumentCommand(ValidBody(), CallerIsAdmin: true),
            CancellationToken.None);

        _db.TermsDocuments.Should().HaveCount(1);
        // Company scope has no anchor entity — no explicit terms-document-*
        // ActivityLog row (repo convention for global settings). The generic
        // auto-captured "created" row from AppDbContext still exists.
        _db.ActivityLogs.Where(l => l.Action.StartsWith("terms-document")).Should().BeEmpty();
    }

    [Fact]
    public async Task Create_UnknownCustomer_ThrowsKeyNotFound()
    {
        var handler = new CreateTermsDocumentHandler(_db);
        var act = () => handler.Handle(new CreateTermsDocumentCommand(
            ValidBody(TermsScope.Customer, customerId: 999), CallerIsAdmin: false),
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── Update handler ────────────────────────────────────────────────────

    [Fact]
    public async Task Update_BodyChange_BumpsVersion_AndLogsRollupRow()
    {
        var customer = new Customer { Name = "Acme" };
        _db.Customers.Add(customer);
        var doc = new TermsDocument
        {
            Scope = TermsScope.Customer,
            CustomerId = customer.Id,
            Title = "Old Title",
            BodyMarkdown = "Old body",
            Version = 1,
            EffectiveFrom = EffectiveFrom,
        };
        _db.TermsDocuments.Add(doc);
        await _db.SaveChangesAsync();

        var handler = new UpdateTermsDocumentHandler(_db);
        var result = await handler.Handle(new UpdateTermsDocumentCommand(
            doc.Id,
            new UpdateTermsDocumentRequestModel(
                Title: "New Title",
                Summary: null,
                BodyMarkdown: "New body",
                EffectiveFrom: EffectiveFrom,
                EffectiveTo: null,
                IsActive: true,
                SortOrder: 0,
                SourceFileAttachmentId: null),
            CallerIsAdmin: false),
            CancellationToken.None);

        result.Version.Should().Be(2);
        result.Title.Should().Be("New Title");

        // Rollup rule: one explicit row covering both fields.
        var log = _db.ActivityLogs.Single(l => l.Action == "terms-document-updated");
        log.Description.Should().Contain("title").And.Contain("bodyMarkdown");
    }

    [Fact]
    public async Task Update_NonBodyChange_DoesNotBumpVersion()
    {
        var doc = new TermsDocument
        {
            Scope = TermsScope.Company,
            Title = "Title",
            BodyMarkdown = "Body",
            Version = 3,
            EffectiveFrom = EffectiveFrom,
            SortOrder = 0,
        };
        _db.TermsDocuments.Add(doc);
        await _db.SaveChangesAsync();

        var handler = new UpdateTermsDocumentHandler(_db);
        var result = await handler.Handle(new UpdateTermsDocumentCommand(
            doc.Id,
            new UpdateTermsDocumentRequestModel(
                Title: "Title",
                Summary: null,
                BodyMarkdown: "Body",
                EffectiveFrom: EffectiveFrom,
                EffectiveTo: null,
                IsActive: true,
                SortOrder: 5,
                SourceFileAttachmentId: null),
            CallerIsAdmin: true),
            CancellationToken.None);

        result.Version.Should().Be(3);
        result.SortOrder.Should().Be(5);
    }

    // ── Delete handler ────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_SoftDeletes_AndLogsOnAnchor()
    {
        var part = new Part { PartNumber = "P-100", Name = "Widget" };
        _db.Parts.Add(part);
        var doc = new TermsDocument
        {
            Scope = TermsScope.Part,
            PartId = part.Id,
            Title = "Part Terms",
            BodyMarkdown = "Body",
            EffectiveFrom = EffectiveFrom,
        };
        _db.TermsDocuments.Add(doc);
        await _db.SaveChangesAsync();

        var handler = new DeleteTermsDocumentHandler(_db, _clock);
        await handler.Handle(new DeleteTermsDocumentCommand(doc.Id, CallerIsAdmin: false), CancellationToken.None);

        _db.TermsDocuments.Should().BeEmpty(); // global soft-delete filter hides it
        var log = _db.ActivityLogs.Single(l => l.Action == "terms-document-removed");
        log.EntityType.Should().Be("Part");
        log.EntityId.Should().Be(part.Id);
    }
}
