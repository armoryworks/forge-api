using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Costing;
using Forge.Core.Enums;
using Forge.Core.Models.Costing;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Costing.Tier3;

/// <summary>Creates an overhead pool on a cost center.</summary>
public record CreateOverheadPoolCommand(
    int CostingCostCenterId,
    int? WorkCenterId,
    string Code,
    string Name,
    OverheadBehavior Behavior,
    decimal? FixedPortion,
    OverheadDriver Driver) : IRequest<OverheadPoolResponseModel>;

public class CreateOverheadPoolValidator : AbstractValidator<CreateOverheadPoolCommand>
{
    public CreateOverheadPoolValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(128);
        RuleFor(x => x.FixedPortion).InclusiveBetween(0m, 1m)
            .When(x => x.Behavior == OverheadBehavior.Semi)
            .WithMessage("A semi-variable pool needs a fixed portion between 0 and 1.");
    }
}

public class CreateOverheadPoolHandler(AppDbContext db)
    : IRequestHandler<CreateOverheadPoolCommand, OverheadPoolResponseModel>
{
    public async Task<OverheadPoolResponseModel> Handle(CreateOverheadPoolCommand request, CancellationToken ct)
    {
        var code = request.Code.Trim();

        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        await db.AcquireAdvisoryLockAsync("overhead-cost-pools.code", $"{request.CostingCostCenterId}:{code}", ct);

        if (await db.OverheadCostPools.AnyAsync(
                p => p.CostingCostCenterId == request.CostingCostCenterId && p.Code == code, ct))
            throw new InvalidOperationException($"An overhead pool with code '{code}' already exists on this cost center.");

        var pool = new OverheadCostPool
        {
            CostingCostCenterId = request.CostingCostCenterId,
            WorkCenterId = request.WorkCenterId,
            Code = code,
            Name = request.Name,
            Behavior = request.Behavior,
            FixedPortion = request.FixedPortion,
            Driver = request.Driver,
        };
        db.OverheadCostPools.Add(pool);
        await db.SaveChangesAsync(ct);

        db.LogActivityAt("created", $"Created overhead pool {pool.Code} ({pool.Behavior}/{pool.Driver})",
            ("OverheadCostPool", pool.Id), ("CostingCostCenter", pool.CostingCostCenterId));
        await db.SaveChangesAsync(ct);

        if (tx is not null)
            await tx.CommitAsync(ct);

        return new OverheadPoolResponseModel(
            pool.Id, pool.CostingCostCenterId, pool.WorkCenterId, pool.Code, pool.Name,
            pool.Behavior.ToString(), pool.FixedPortion, pool.Driver.ToString());
    }
}
