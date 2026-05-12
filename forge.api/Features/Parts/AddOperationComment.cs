using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Parts;

public record AddOperationCommentCommand(int PartId, int OperationId, string Comment) : IRequest;

public class AddOperationCommentValidator : AbstractValidator<AddOperationCommentCommand>
{
    public AddOperationCommentValidator()
    {
        RuleFor(x => x.Comment).NotEmpty().MaximumLength(2000);
    }
}

public class AddOperationCommentHandler(
    AppDbContext db,
    IActivityLogRepository activityRepo,
    IHttpContextAccessor httpContextAccessor) : IRequestHandler<AddOperationCommentCommand>
{
    public async Task Handle(AddOperationCommentCommand request, CancellationToken cancellationToken)
    {
        var operation = await db.Operations.FirstOrDefaultAsync(
            o => o.Id == request.OperationId && o.PartId == request.PartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Operation {request.OperationId} not found for part {request.PartId}");

        var userId = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

        var log = new ActivityLog
        {
            EntityType = "Operation",
            EntityId = operation.Id,
            UserId = userId is not null ? int.Parse(userId) : null,
            Action = "Comment",
            Description = request.Comment.Trim(),
        };

        await activityRepo.AddAsync(log, cancellationToken);
        await activityRepo.SaveChangesAsync(cancellationToken);
    }
}
