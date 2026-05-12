using MediatR;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.FollowUpTasks;

public record CompleteFollowUpTaskCommand(int Id, int UserId) : IRequest;

public class CompleteFollowUpTaskHandler(
    AppDbContext db,
    IClock clock) : IRequestHandler<CompleteFollowUpTaskCommand>
{
    public async Task Handle(CompleteFollowUpTaskCommand request, CancellationToken ct)
    {
        var task = await db.FollowUpTasks.FindAsync([request.Id], ct)
            ?? throw new KeyNotFoundException($"FollowUpTask {request.Id} not found");

        if (task.AssignedToUserId != request.UserId)
            throw new UnauthorizedAccessException("Cannot complete a task assigned to another user");

        task.Status = FollowUpStatus.Completed;
        task.CompletedAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);
    }
}
