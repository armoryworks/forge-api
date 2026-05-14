using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record UpdateCapaTaskCommand(int CapaId, int TaskId, UpdateCapaTaskRequestModel Request) : IRequest;

public class UpdateCapaTaskHandler(
    AppDbContext db,
    IClock clock,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<UpdateCapaTaskCommand>
{
    public async Task Handle(UpdateCapaTaskCommand command, CancellationToken cancellationToken)
    {
        var task = await db.CapaTasks
            .FirstOrDefaultAsync(t => t.Id == command.TaskId && t.CapaId == command.CapaId, cancellationToken)
            ?? throw new KeyNotFoundException($"Task {command.TaskId} not found for CAPA {command.CapaId}");

        var req = command.Request;

        if (req.Title != null) task.Title = req.Title;
        if (req.Description != null) task.Description = req.Description;
        if (req.AssigneeId.HasValue) task.AssigneeId = req.AssigneeId.Value;
        if (req.DueDate.HasValue) task.DueDate = req.DueDate.Value;
        if (req.CompletionNotes != null) task.CompletionNotes = req.CompletionNotes;

        if (req.Status.HasValue)
        {
            task.Status = req.Status.Value;
            if (req.Status.Value == CapaTaskStatus.Completed)
            {
                var userId = int.Parse(httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                task.CompletedAt = clock.UtcNow;
                task.CompletedById = userId;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
