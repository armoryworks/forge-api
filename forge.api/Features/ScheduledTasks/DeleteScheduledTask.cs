using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Data.Context;

namespace Forge.Api.Features.ScheduledTasks;

public record DeleteScheduledTaskCommand(int Id) : IRequest;

public class DeleteScheduledTaskHandler(AppDbContext db) : IRequestHandler<DeleteScheduledTaskCommand>
{
    public async Task Handle(DeleteScheduledTaskCommand request, CancellationToken ct)
    {
        var task = await db.ScheduledTasks.FirstOrDefaultAsync(t => t.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Scheduled task {request.Id} not found.");

        task.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
