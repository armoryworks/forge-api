using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record GetJobStartPlanQuery(int PartId, decimal Quantity, int? SalesOrderLineId = null, int? JobId = null)
    : IRequest<JobStartPlanResponseModel>;

public class GetJobStartPlanQueryValidator : AbstractValidator<GetJobStartPlanQuery>
{
    public GetJobStartPlanQueryValidator()
    {
        RuleFor(x => x.PartId).GreaterThan(0);
        RuleFor(x => x.Quantity).GreaterThan(0m).JobQuantityLimits();
    }
}

public class GetJobStartPlanHandler(AppDbContext db) : IRequestHandler<GetJobStartPlanQuery, JobStartPlanResponseModel>
{
    public async Task<JobStartPlanResponseModel> Handle(GetJobStartPlanQuery request, CancellationToken cancellationToken)
    {
        var job = request.JobId is int jobId
            ? await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken)
                ?? throw new KeyNotFoundException($"Job with ID {jobId} not found.")
            : new Job { SalesOrderLineId = request.SalesOrderLineId };
        if (request.SalesOrderLineId is not null)
            job.SalesOrderLineId ??= request.SalesOrderLineId;

        var start = await ProductionQuantityPlan.CalculatedStartAsync(
            db, job, request.PartId, request.Quantity, cancellationToken);
        var extra = (int)Math.Min(JobQuantityRules.Max, Math.Max(0m, Math.Ceiling(start - request.Quantity)));
        return new JobStartPlanResponseModel(request.Quantity, start, extra);
    }
}
