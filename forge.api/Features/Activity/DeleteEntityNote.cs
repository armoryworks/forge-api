using MediatR;

using Forge.Api.Authorization;
using Forge.Data.Context;

namespace Forge.Api.Features.Activity;

public record DeleteEntityNoteCommand(string EntityType, int EntityId, int NoteId) : IRequest;

public class DeleteEntityNoteHandler(AppDbContext db) : IRequestHandler<DeleteEntityNoteCommand>
{
    public async Task Handle(DeleteEntityNoteCommand request, CancellationToken ct)
    {
        var note = await db.EntityNotes.FindAsync([request.NoteId], ct);
        if (note is null
            || note.EntityId != request.EntityId
            || EntityAccessRules.Normalize(note.EntityType) != EntityAccessRules.Normalize(request.EntityType))
            throw new KeyNotFoundException($"Note {request.NoteId} not found");

        db.EntityNotes.Remove(note);
        await db.SaveChangesAsync(ct);
    }
}
