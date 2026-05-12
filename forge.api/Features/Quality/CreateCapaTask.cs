using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record CreateCapaTaskCommand(int CapaId, CreateCapaTaskRequestModel Request) : IRequest<CapaTaskResponseModel>;

public class CreateCapaTaskValidator : AbstractValidator<CreateCapaTaskCommand>
{
    public CreateCapaTaskValidator()
    {
        RuleFor(x => x.Request.Title).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Request.AssigneeId).GreaterThan(0);
    }
}

public class CreateCapaTaskHandler(AppDbContext db)
    : IRequestHandler<CreateCapaTaskCommand, CapaTaskResponseModel>
{
    public async Task<CapaTaskResponseModel> Handle(
        CreateCapaTaskCommand command, CancellationToken cancellationToken)
    {
        var capa = await db.CorrectiveActions
            .AsNoTracking()
            .AnyAsync(c => c.Id == command.CapaId, cancellationToken);

        if (!capa)
            throw new KeyNotFoundException($"CAPA {command.CapaId} not found");

        var maxSortOrder = await db.CapaTasks
            .Where(t => t.CapaId == command.CapaId)
            .MaxAsync(t => (int?)t.SortOrder, cancellationToken) ?? 0;

        var task = new CapaTask
        {
            CapaId = command.CapaId,
            Title = command.Request.Title,
            Description = command.Request.Description,
            AssigneeId = command.Request.AssigneeId,
            DueDate = command.Request.DueDate,
            SortOrder = maxSortOrder + 1,
        };

        db.CapaTasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);

        var assigneeName = await db.Users
            .Where(u => u.Id == task.AssigneeId)
            .Select(u => $"{u.LastName}, {u.FirstName}")
            .FirstOrDefaultAsync(cancellationToken) ?? "Unknown";

        return new CapaTaskResponseModel
        {
            Id = task.Id,
            CapaId = task.CapaId,
            Title = task.Title,
            Description = task.Description,
            AssigneeId = task.AssigneeId,
            AssigneeName = assigneeName,
            DueDate = task.DueDate,
            Status = task.Status,
            SortOrder = task.SortOrder,
        };
    }
}
