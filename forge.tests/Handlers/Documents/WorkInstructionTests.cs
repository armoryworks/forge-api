using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;

using Forge.Api.Features.Documents;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Documents;

/// <summary>
/// A work instruction is a controlled document tied to a part or a routing operation; the shop floor sees the
/// current approved revision of the released ones for the operation it is working (operation-level first, then
/// part-level), never a draft or an obsolete one.
/// </summary>
public class WorkInstructionTests
{
    private const int AuthorId = 11;

    private static IHttpContextAccessor Http(int userId = AuthorId) => new HttpContextAccessor
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())])),
        },
    };

    private static async Task<(Part Part, Operation Op10, Operation Op20)> SeedRoutingAsync(AppDbContext db)
    {
        var part = new Part { PartNumber = "SA-210", Name = "Shaft" };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        var op10 = new Operation { PartId = part.Id, StepNumber = 10, Title = "Turn" };
        var op20 = new Operation { PartId = part.Id, StepNumber = 20, Title = "Grind" };
        db.Operations.AddRange(op10, op20);
        await db.SaveChangesAsync();
        return (part, op10, op20);
    }

    private static async Task<int> CreateAsync(AppDbContext db, string title, int? partId, int? operationId, string category = "WI")
    {
        var created = await new CreateControlledDocumentHandler(db, Http())
            .Handle(new CreateControlledDocumentCommand(title, null, category, 365, partId, operationId), CancellationToken.None);
        return created.Id;
    }

    private static async Task ReleaseAsync(AppDbContext db, int documentId)
    {
        var file = new FileAttachment
        {
            FileName = $"wi-{documentId}.pdf", ContentType = "application/pdf", BucketName = "b", ObjectKey = $"k{documentId}",
            EntityType = "controlled-documents", EntityId = documentId, UploadedById = AuthorId,
        };
        db.FileAttachments.Add(file);
        await db.SaveChangesAsync();
        var revision = await new CreateDocumentRevisionHandler(db, Http())
            .Handle(new CreateDocumentRevisionCommand(documentId, file.Id, "Release"), CancellationToken.None);
        await new SubmitDocumentRevisionHandler(db)
            .Handle(new SubmitDocumentRevisionCommand(documentId, revision.Id), CancellationToken.None);
        await new ApproveDocumentRevisionHandler(db, Http(22), new SystemClock())
            .Handle(new ApproveDocumentRevisionCommand(documentId, revision.Id), CancellationToken.None);
    }

    [Fact]
    public async Task Creating_with_only_an_operation_links_its_part_too()
    {
        using var db = TestDbContextFactory.Create();
        var (part, op10, _) = await SeedRoutingAsync(db);

        var created = await new CreateControlledDocumentHandler(db, Http())
            .Handle(new CreateControlledDocumentCommand("Turning WI", null, "WI", 365, null, op10.Id), CancellationToken.None);

        created.PartId.Should().Be(part.Id);
        created.PartNumber.Should().Be("SA-210");
        created.OperationId.Should().Be(op10.Id);
        created.OperationLabel.Should().Be("op 10 — Turn");
    }

    [Fact]
    public async Task An_operation_of_another_part_is_refused()
    {
        using var db = TestDbContextFactory.Create();
        var (_, op10, _) = await SeedRoutingAsync(db);
        var other = new Part { PartNumber = "OTHER", Name = "Other" };
        db.Parts.Add(other);
        await db.SaveChangesAsync();

        var act = () => new CreateControlledDocumentHandler(db, Http())
            .Handle(new CreateControlledDocumentCommand("Mismatch", null, "WI", 365, other.Id, op10.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task The_operation_shows_its_released_instructions_then_the_parts()
    {
        using var db = TestDbContextFactory.Create();
        var (part, op10, op20) = await SeedRoutingAsync(db);
        var forOp10 = await CreateAsync(db, "Turning WI", null, op10.Id);
        var forPart = await CreateAsync(db, "Handling WI", part.Id, null);
        var forOp20 = await CreateAsync(db, "Grinding WI", null, op20.Id);
        var draft = await CreateAsync(db, "Draft WI", null, op10.Id);
        var sop = await CreateAsync(db, "Safety SOP", null, op10.Id, "SOP");
        await ReleaseAsync(db, forOp10);
        await ReleaseAsync(db, forPart);
        await ReleaseAsync(db, forOp20);
        await ReleaseAsync(db, sop);

        var shown = await new GetCurrentWorkInstructionsHandler(db)
            .Handle(new GetCurrentWorkInstructionsQuery(op10.Id, null), CancellationToken.None);

        shown.Select(w => w.DocumentId).Should().Equal(forOp10, forPart);
        shown[0].RevisionNumber.Should().Be(1);
        shown[0].FileName.Should().Be($"wi-{forOp10}.pdf");
        shown.Should().NotContain(w => w.DocumentId == draft);
    }

    [Fact]
    public async Task An_obsolete_instruction_is_no_longer_shown()
    {
        using var db = TestDbContextFactory.Create();
        var (_, op10, _) = await SeedRoutingAsync(db);
        var id = await CreateAsync(db, "Turning WI", null, op10.Id);
        await ReleaseAsync(db, id);

        await new ObsoleteControlledDocumentHandler(db)
            .Handle(new ObsoleteControlledDocumentCommand(id), CancellationToken.None);

        (await new GetCurrentWorkInstructionsHandler(db)
            .Handle(new GetCurrentWorkInstructionsQuery(op10.Id, null), CancellationToken.None))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task The_document_list_filters_by_operation()
    {
        using var db = TestDbContextFactory.Create();
        var (_, op10, op20) = await SeedRoutingAsync(db);
        var forOp10 = await CreateAsync(db, "Turning WI", null, op10.Id);
        await CreateAsync(db, "Grinding WI", null, op20.Id);

        var list = await new GetControlledDocumentsHandler(db, Http())
            .Handle(new GetControlledDocumentsQuery(null, null, null, op10.Id), CancellationToken.None);

        list.Select(d => d.Id).Should().Equal(forOp10);
        list[0].OperationLabel.Should().Be("op 10 — Turn");
    }

    [Fact]
    public async Task Revisions_name_their_author_approver_and_file()
    {
        using var db = TestDbContextFactory.Create();
        db.Users.AddRange(
            new ApplicationUser { Id = AuthorId, UserName = "a@t.com", FirstName = "Ana", LastName = "Author" },
            new ApplicationUser { Id = 22, UserName = "b@t.com", FirstName = "Ben", LastName = "Approver" });
        await db.SaveChangesAsync();
        var (part, _, _) = await SeedRoutingAsync(db);
        var id = await CreateAsync(db, "Handling WI", part.Id, null);
        await ReleaseAsync(db, id);

        var revisions = await new GetDocumentRevisionsHandler(db, Http())
            .Handle(new GetDocumentRevisionsQuery(id), CancellationToken.None);

        var revision = revisions.Should().ContainSingle().Subject;
        revision.AuthoredByName.Should().Be("Author, Ana");
        revision.ApprovedByName.Should().Be("Approver, Ben");
        revision.FileName.Should().Be($"wi-{id}.pdf");
    }
}
