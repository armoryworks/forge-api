using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.AiAssistants;

public record DeleteAiAssistantCommand(int Id) : IRequest;

public class DeleteAiAssistantHandler(AppDbContext db) : IRequestHandler<DeleteAiAssistantCommand>
{
    public async Task Handle(DeleteAiAssistantCommand request, CancellationToken ct)
    {
        var entity = await db.AiAssistants
            .FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"AI Assistant with ID {request.Id} not found.");

        if (entity.IsBuiltIn)
            throw new InvalidOperationException("Built-in assistants cannot be deleted. Disable them instead.");

        entity.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
