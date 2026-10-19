using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Costing;
using Forge.Core.Enums;
using Forge.Core.Models.Costing;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Costing.Tier3;

/// <summary>Opens a new costing period.</summary>
public record CreateCostingPeriodCommand(DateTime StartDate, DateTime EndDate)
    : IRequest<CostingPeriodResponseModel>;

public class CreateCostingPeriodValidator : AbstractValidator<CreateCostingPeriodCommand>
{
    public CreateCostingPeriodValidator()
    {
        RuleFor(x => x.EndDate).GreaterThan(x => x.StartDate)
            .WithMessage("End date must be after start date.");
    }
}

public class CreateCostingPeriodHandler(AppDbContext db)
    : IRequestHandler<CreateCostingPeriodCommand, CostingPeriodResponseModel>
{
    public async Task<CostingPeriodResponseModel> Handle(CreateCostingPeriodCommand request, CancellationToken ct)
    {
        var start = DateTime.SpecifyKind(request.StartDate, DateTimeKind.Utc);
        var end = DateTime.SpecifyKind(request.EndDate, DateTimeKind.Utc);

        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        await db.AcquireAdvisoryLockAsync("costing-periods.create", string.Empty, ct);

        var overlapping = await db.CostingPeriods
            .Where(p => p.StartDate < end && p.EndDate > start)
            .OrderBy(p => p.StartDate)
            .FirstOrDefaultAsync(ct);
        if (overlapping is not null)
            throw new InvalidOperationException(
                $"This period overlaps the costing period {overlapping.StartDate:MM/dd/yyyy}–{overlapping.EndDate:MM/dd/yyyy}.");

        var period = new CostingPeriod
        {
            StartDate = start,
            EndDate = end,
            Status = CostingPeriodStatus.Open,
        };
        db.CostingPeriods.Add(period);
        await db.SaveChangesAsync(ct);

        db.LogActivityAt("created", $"Opened costing period {period.StartDate:MM/dd/yyyy}–{period.EndDate:MM/dd/yyyy}",
            ("CostingPeriod", period.Id));
        await db.SaveChangesAsync(ct);

        if (tx is not null)
            await tx.CommitAsync(ct);

        return new CostingPeriodResponseModel(
            period.Id, period.StartDate, period.EndDate, period.Status.ToString(), period.FrozenAt, period.ClosedAt);
    }
}
