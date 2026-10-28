using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Inventory;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record GetJobSurplusSplitQuery(int JobId, decimal Quantity, int? RunId = null)
    : IRequest<JobSurplusSplitResponseModel>;

public class GetJobSurplusSplitQueryValidator : AbstractValidator<GetJobSurplusSplitQuery>
{
    public GetJobSurplusSplitQueryValidator()
    {
        RuleFor(x => x.JobId).GreaterThan(0);
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(0m).JobQuantityLimits();
    }
}

public class GetJobSurplusSplitHandler(AppDbContext db, ISettingsService settings)
    : IRequestHandler<GetJobSurplusSplitQuery, JobSurplusSplitResponseModel>
{
    public async Task<JobSurplusSplitResponseModel> Handle(
        GetJobSurplusSplitQuery request, CancellationToken cancellationToken)
    {
        var partId = await db.Jobs.AsNoTracking()
            .Where(j => j.Id == request.JobId)
            .Select(j => new { j.PartId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Job with ID {request.JobId} not found.");

        if (partId.PartId is not int part)
            return new JobSurplusSplitResponseModel(request.Quantity, 0m, null, null);

        var split = await SurplusStock.PlanAsync(
            db, settings, request.JobId, part, request.Quantity, null, request.RunId, cancellationToken);
        return new JobSurplusSplitResponseModel(
            split.DemandQuantity, split.SurplusQuantity, split.SurplusLocation?.Id, split.SurplusLocation?.Name);
    }
}
