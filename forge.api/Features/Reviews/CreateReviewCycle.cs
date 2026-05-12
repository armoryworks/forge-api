using FluentValidation;
using MediatR;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Reviews;

public record CreateReviewCycleCommand(CreateReviewCycleRequestModel Request) : IRequest<ReviewCycleResponseModel>;

public class CreateReviewCycleValidator : AbstractValidator<CreateReviewCycleCommand>
{
    public CreateReviewCycleValidator()
    {
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.StartDate).NotEmpty();
        RuleFor(x => x.Request.EndDate).GreaterThan(x => x.Request.StartDate);
    }
}

public class CreateReviewCycleHandler(AppDbContext db) : IRequestHandler<CreateReviewCycleCommand, ReviewCycleResponseModel>
{
    public async Task<ReviewCycleResponseModel> Handle(CreateReviewCycleCommand request, CancellationToken cancellationToken)
    {
        var cycle = new ReviewCycle
        {
            Name = request.Request.Name.Trim(),
            StartDate = request.Request.StartDate,
            EndDate = request.Request.EndDate,
            Description = request.Request.Description?.Trim(),
        };

        db.ReviewCycles.Add(cycle);
        await db.SaveChangesAsync(cancellationToken);

        return new ReviewCycleResponseModel(
            cycle.Id, cycle.Name, cycle.StartDate, cycle.EndDate,
            cycle.Status, cycle.Description, 0);
    }
}
