using FluentAssertions;

using Forge.Api.Features.Activity;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Activity;

/// <summary>
/// The delete-note route names an entity, and the controller checks the caller's access to
/// that entity, so the note must belong to it; otherwise a caller could delete any note by id
/// through a record they can open.
/// </summary>
public class DeleteEntityNoteHandlerTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    [Fact]
    public async Task A_note_on_another_record_is_not_found()
    {
        var note = new EntityNote { EntityType = "Invoice", EntityId = 7, Text = "credit hold" };
        _db.EntityNotes.Add(note);
        await _db.SaveChangesAsync();

        var act = () => new DeleteEntityNoteHandler(_db)
            .Handle(new DeleteEntityNoteCommand("Job", 1, note.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        _db.EntityNotes.Should().ContainSingle();
    }

    [Fact]
    public async Task A_note_on_the_named_record_is_deleted()
    {
        var note = new EntityNote { EntityType = "Job", EntityId = 1, Text = "check fixture" };
        _db.EntityNotes.Add(note);
        await _db.SaveChangesAsync();

        await new DeleteEntityNoteHandler(_db)
            .Handle(new DeleteEntityNoteCommand("job", 1, note.Id), CancellationToken.None);

        _db.EntityNotes.Should().BeEmpty();
    }
}
