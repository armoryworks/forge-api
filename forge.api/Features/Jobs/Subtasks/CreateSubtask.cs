using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Jobs.Subtasks;

public record CreateSubtaskCommand(
    int JobId,
    string Text,
    int? AssigneeId) : IRequest<SubtaskResponseModel>;

public class CreateSubtaskCommandValidator : AbstractValidator<CreateSubtaskCommand>
{
    public CreateSubtaskCommandValidator()
    {
        RuleFor(x => x.JobId).GreaterThan(0);
        RuleFor(x => x.Text).NotEmpty().MaximumLength(500);
    }
}

public class CreateSubtaskHandler(
    ISubtaskRepository repo,
    IHubContext<BoardHub> boardHub) : IRequestHandler<CreateSubtaskCommand, SubtaskResponseModel>
{
    public async Task<SubtaskResponseModel> Handle(CreateSubtaskCommand request, CancellationToken cancellationToken)
    {
        var jobExists = await repo.JobExistsAsync(request.JobId, cancellationToken);
        if (!jobExists)
            throw new KeyNotFoundException($"Job with ID {request.JobId} not found.");

        var maxSortOrder = await repo.GetMaxSortOrderAsync(request.JobId, cancellationToken);

        var subtask = new JobSubtask
        {
            JobId = request.JobId,
            Text = request.Text,
            AssigneeId = request.AssigneeId,
            SortOrder = maxSortOrder + 1,
        };

        await repo.AddAsync(subtask, cancellationToken);
        await repo.SaveChangesAsync(cancellationToken);

        var result = new SubtaskResponseModel(
            subtask.Id,
            subtask.JobId,
            subtask.Text,
            subtask.IsCompleted,
            subtask.AssigneeId,
            subtask.SortOrder,
            subtask.CompletedAt);

        // Broadcast to job detail subscribers
        await boardHub.Clients.Group($"job:{request.JobId}")
            .SendAsync("subtaskChanged", new { jobId = request.JobId, subtask = result, changeType = "created" }, cancellationToken);

        return result;
    }
}
