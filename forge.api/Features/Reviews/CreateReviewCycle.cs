using FluentValidation;
using MediatR;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Reviews;

public record CreateReviewCycleCommand(CreateReviewCycleRequestModel Request) : IRequest<ReviewCycleResponseModel>;

public class CreateReviewCycleValidator : AbstractValidator<CreateReviewCycleCommand>
{
    public CreateReviewCycleValidator()
    {
        ReviewCycleRules.Apply(this,
            x => x.Request.Name, x => x.Request.StartDate, x => x.Request.EndDate,
            x => x.Request.RatingScaleMax, x => x.Request.Competencies,
            x => x.Request.ScopeType, x => x.Request.ScopeDepartment, x => x.Request.ScopeUserIds);
    }
}

public class CreateReviewCycleHandler(AppDbContext db) : IRequestHandler<CreateReviewCycleCommand, ReviewCycleResponseModel>
{
    public async Task<ReviewCycleResponseModel> Handle(CreateReviewCycleCommand request, CancellationToken cancellationToken)
    {
        var r = request.Request;
        var cycle = new ReviewCycle
        {
            Name = r.Name.Trim(),
            StartDate = r.StartDate,
            EndDate = r.EndDate,
            Description = r.Description?.Trim(),
            RatingScaleMax = r.RatingScaleMax ?? 5,
            CompetenciesJson = ReviewJson.Write(ReviewCycleRules.CleanCompetencies(r.Competencies)),
            ScopeType = r.ScopeType ?? ReviewScopeTypes.All,
            ScopeDepartment = r.ScopeType == ReviewScopeTypes.Department ? r.ScopeDepartment?.Trim() : null,
            ScopeUserIdsJson = r.ScopeType == ReviewScopeTypes.List ? ReviewJson.Write(r.ScopeUserIds?.Distinct().ToList()) : null,
        };

        db.ReviewCycles.Add(cycle);
        await db.SaveChangesAsync(cancellationToken);
        db.LogActivityAt("created", $"Review cycle {cycle.Name} created", ("ReviewCycle", cycle.Id));
        await db.SaveChangesAsync(cancellationToken);

        return ReviewProjection.ToCycleModel(cycle, []);
    }
}
