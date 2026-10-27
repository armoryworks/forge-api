using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Reviews;

public record UpdateReviewCycleCommand(int Id, UpdateReviewCycleRequestModel Request) : IRequest<ReviewCycleResponseModel>;

public class UpdateReviewCycleValidator : AbstractValidator<UpdateReviewCycleCommand>
{
    public UpdateReviewCycleValidator()
    {
        ReviewCycleRules.Apply(this,
            x => x.Request.Name, x => x.Request.StartDate, x => x.Request.EndDate,
            x => x.Request.RatingScaleMax, x => x.Request.Competencies,
            x => x.Request.ScopeType, x => x.Request.ScopeDepartment, x => x.Request.ScopeUserIds);
    }
}

/// <summary>
/// Edits a cycle. Scope, rating scale and competencies are fixed once the cycle is launched
/// (reviews were created against them); name, dates and description stay editable until it closes.
/// </summary>
public class UpdateReviewCycleHandler(AppDbContext db) : IRequestHandler<UpdateReviewCycleCommand, ReviewCycleResponseModel>
{
    public async Task<ReviewCycleResponseModel> Handle(UpdateReviewCycleCommand command, CancellationToken ct)
    {
        var cycle = await db.ReviewCycles.FirstOrDefaultAsync(c => c.Id == command.Id, ct)
            ?? throw new KeyNotFoundException($"Review cycle {command.Id} not found");
        if (cycle.Status == ReviewCycleStatus.Completed)
            throw new InvalidOperationException("A closed review cycle cannot be edited.");

        var r = command.Request;
        cycle.Name = r.Name.Trim();
        cycle.StartDate = r.StartDate;
        cycle.EndDate = r.EndDate;
        cycle.Description = r.Description?.Trim();

        if (cycle.Status == ReviewCycleStatus.Draft)
        {
            cycle.RatingScaleMax = r.RatingScaleMax;
            cycle.CompetenciesJson = ReviewJson.Write(ReviewCycleRules.CleanCompetencies(r.Competencies));
            cycle.ScopeType = r.ScopeType ?? ReviewScopeTypes.All;
            cycle.ScopeDepartment = cycle.ScopeType == ReviewScopeTypes.Department ? r.ScopeDepartment?.Trim() : null;
            cycle.ScopeUserIdsJson = cycle.ScopeType == ReviewScopeTypes.List ? ReviewJson.Write(r.ScopeUserIds?.Distinct().ToList()) : null;
        }

        db.ChangeTracker.DetectChanges();
        var changed = ActivityChangeSummary.ChangedProperties(db.Entry(cycle));
        if (changed.Count > 0)
            db.LogActivityAt("updated", ActivityChangeSummary.Updated(changed) ?? "Updated", ("ReviewCycle", cycle.Id));
        await db.SaveChangesAsync(ct);

        var statuses = await db.PerformanceReviews.Where(p => p.CycleId == cycle.Id).Select(p => p.Status).ToListAsync(ct);
        return ReviewProjection.ToCycleModel(cycle, statuses);
    }
}
