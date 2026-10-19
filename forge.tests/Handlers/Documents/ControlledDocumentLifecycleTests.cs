using System.Reflection;
using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

using Forge.Api.Controllers;
using Forge.Api.Features.Documents;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Documents;

public class ControlledDocumentLifecycleTests
{
    private const int AuthorId = 11;
    private const int ApproverId = 22;

    private static IHttpContextAccessor Http(int userId) => new HttpContextAccessor
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())])),
        },
    };

    private static async Task<(ControlledDocument Document, FileAttachment File)> SeedAsync(AppDbContext db)
    {
        var document = new ControlledDocument { DocumentNumber = "DOC-00001", Title = "WI SA-210 OP20", Category = "WI", OwnerId = AuthorId, ReviewIntervalDays = 365 };
        var file = new FileAttachment { FileName = "wi.pdf", ContentType = "application/pdf", BucketName = "b", ObjectKey = "k", EntityType = "controlled-documents", EntityId = 1, UploadedById = AuthorId };
        db.ControlledDocuments.Add(document);
        db.FileAttachments.Add(file);
        await db.SaveChangesAsync();
        return (document, file);
    }

    private static async Task<int> ReleaseRevisionAsync(AppDbContext db, int documentId, int fileId, string change)
    {
        var created = await new CreateDocumentRevisionHandler(db, Http(AuthorId))
            .Handle(new CreateDocumentRevisionCommand(documentId, fileId, change), CancellationToken.None);
        await new SubmitDocumentRevisionHandler(db)
            .Handle(new SubmitDocumentRevisionCommand(documentId, created.Id), CancellationToken.None);
        await new ApproveDocumentRevisionHandler(db, Http(ApproverId), new SystemClock())
            .Handle(new ApproveDocumentRevisionCommand(documentId, created.Id), CancellationToken.None);
        return created.Id;
    }

    [Fact]
    public async Task Second_approved_revision_supersedes_the_first_and_becomes_current()
    {
        using var db = TestDbContextFactory.Create();
        var (document, file) = await SeedAsync(db);

        var first = await ReleaseRevisionAsync(db, document.Id, file.Id, "Initial release");
        document.Status.Should().Be(ControlledDocumentStatus.Released);
        document.CurrentRevision.Should().Be(1);
        document.ReleasedAt.Should().NotBeNull();
        document.ReviewDueDate.Should().BeCloseTo(document.ReleasedAt!.Value.AddDays(365), TimeSpan.FromSeconds(1));

        var second = await ReleaseRevisionAsync(db, document.Id, file.Id, "ECO-2 chamfer");

        var revisions = await new GetDocumentRevisionsHandler(db, Http(AuthorId))
            .Handle(new GetDocumentRevisionsQuery(document.Id), CancellationToken.None);
        revisions.Should().HaveCount(2);
        revisions.Single(r => r.Id == first).Status.Should().Be(DocumentRevisionStatus.Superseded);
        var current = revisions.Single(r => r.Id == second);
        current.Status.Should().Be(DocumentRevisionStatus.Approved);
        current.RevisionNumber.Should().Be(2);
        current.AuthoredById.Should().Be(AuthorId);
        current.ApprovedById.Should().Be(ApproverId);
        document.CurrentRevision.Should().Be(2);
    }

    [Fact]
    public async Task Only_one_open_revision_at_a_time()
    {
        using var db = TestDbContextFactory.Create();
        var (document, file) = await SeedAsync(db);
        var handler = new CreateDocumentRevisionHandler(db, Http(AuthorId));
        await handler.Handle(new CreateDocumentRevisionCommand(document.Id, file.Id, "Draft A"), CancellationToken.None);

        var act = () => handler.Handle(new CreateDocumentRevisionCommand(document.Id, file.Id, "Draft B"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Rejected_first_revision_returns_the_document_to_draft()
    {
        using var db = TestDbContextFactory.Create();
        var (document, file) = await SeedAsync(db);
        var created = await new CreateDocumentRevisionHandler(db, Http(AuthorId))
            .Handle(new CreateDocumentRevisionCommand(document.Id, file.Id, "Initial"), CancellationToken.None);
        await new SubmitDocumentRevisionHandler(db).Handle(new SubmitDocumentRevisionCommand(document.Id, created.Id), CancellationToken.None);
        document.Status.Should().Be(ControlledDocumentStatus.InReview);

        var rejected = await new RejectDocumentRevisionHandler(db, Http(ApproverId))
            .Handle(new RejectDocumentRevisionCommand(document.Id, created.Id), CancellationToken.None);

        rejected.Status.Should().Be(DocumentRevisionStatus.Rejected);
        rejected.ReviewedById.Should().Be(ApproverId);
        document.Status.Should().Be(ControlledDocumentStatus.Draft);
    }

    [Fact]
    public async Task Approving_a_draft_revision_is_refused()
    {
        using var db = TestDbContextFactory.Create();
        var (document, file) = await SeedAsync(db);
        var created = await new CreateDocumentRevisionHandler(db, Http(AuthorId))
            .Handle(new CreateDocumentRevisionCommand(document.Id, file.Id, "Initial"), CancellationToken.None);

        var act = () => new ApproveDocumentRevisionHandler(db, Http(ApproverId), new SystemClock())
            .Handle(new ApproveDocumentRevisionCommand(document.Id, created.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Obsolete_document_cannot_be_revised()
    {
        using var db = TestDbContextFactory.Create();
        var (document, file) = await SeedAsync(db);
        await ReleaseRevisionAsync(db, document.Id, file.Id, "Initial");
        await new ObsoleteControlledDocumentHandler(db).Handle(new ObsoleteControlledDocumentCommand(document.Id), CancellationToken.None);
        document.Status.Should().Be(ControlledDocumentStatus.Obsolete);

        var act = () => new CreateDocumentRevisionHandler(db, Http(AuthorId))
            .Handle(new CreateDocumentRevisionCommand(document.Id, file.Id, "Late change"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Revision_needs_an_existing_file()
    {
        using var db = TestDbContextFactory.Create();
        var (document, _) = await SeedAsync(db);

        var act = () => new CreateDocumentRevisionHandler(db, Http(AuthorId))
            .Handle(new CreateDocumentRevisionCommand(document.Id, 9999, "Initial"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData(nameof(ControlledDocumentsController.CreateDocument))]
    [InlineData(nameof(ControlledDocumentsController.CreateRevision))]
    [InlineData(nameof(ControlledDocumentsController.SubmitRevision))]
    [InlineData(nameof(ControlledDocumentsController.ApproveRevision))]
    [InlineData(nameof(ControlledDocumentsController.RejectRevision))]
    [InlineData(nameof(ControlledDocumentsController.ObsoleteDocument))]
    public void Shop_floor_roles_cannot_change_controlled_documents(string action)
    {
        var roles = RolesFor(action);

        roles.Should().NotBeEmpty();
        roles.Should().NotContain(["ProductionWorker", "Production Manager", "Production Planner"]);
    }

    [Theory]
    [InlineData(nameof(ControlledDocumentsController.ApproveRevision))]
    [InlineData(nameof(ControlledDocumentsController.RejectRevision))]
    [InlineData(nameof(ControlledDocumentsController.ObsoleteDocument))]
    public void Engineers_author_but_do_not_release(string action)
    {
        RolesFor(action).Should().NotContain("Engineer");
        RolesFor(nameof(ControlledDocumentsController.CreateRevision)).Should().Contain("Engineer");
    }

    [Theory]
    [InlineData(nameof(ControlledDocumentsController.GetDocuments))]
    [InlineData(nameof(ControlledDocumentsController.GetRevisions))]
    public void Every_signed_in_user_reads_controlled_documents(string action)
    {
        RolesFor(action).Should().BeEmpty();
    }

    private static string[] RolesFor(string action) =>
        typeof(ControlledDocumentsController).GetMethod(action)!
            .GetCustomAttributes<AuthorizeAttribute>()
            .SelectMany(a => (a.Roles ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
}
