using FluentValidation;
using MediatR;

using Forge.Core.Entities.Costing;
using Forge.Core.Enums;
using Forge.Core.Models.Costing;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Costing.Tier3;

/// <summary>Creates a costing cost center.</summary>
public record CreateCostingCostCenterCommand(
    string Code,
    string Name,
    CostCenterType Type,
    int? ParentId,
    decimal? Sqft,
    decimal? Headcount,
    bool IsInventoriable) : IRequest<CostingCostCenterResponseModel>;

public class CreateCostingCostCenterValidator : AbstractValidator<CreateCostingCostCenterCommand>
{
    public CreateCostingCostCenterValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(128);
    }
}

public class CreateCostingCostCenterHandler(AppDbContext db)
    : IRequestHandler<CreateCostingCostCenterCommand, CostingCostCenterResponseModel>
{
    public async Task<CostingCostCenterResponseModel> Handle(CreateCostingCostCenterCommand request, CancellationToken ct)
    {
        var cc = new CostingCostCenter
        {
            Code = request.Code,
            Name = request.Name,
            Type = request.Type,
            ParentId = request.ParentId,
            Sqft = request.Sqft,
            Headcount = request.Headcount,
            IsInventoriable = request.IsInventoriable,
        };
        db.CostingCostCenters.Add(cc);
        await db.SaveChangesAsync(ct);

        db.LogActivityAt("created", $"Created cost center {cc.Code} ({cc.Type})", ("CostingCostCenter", cc.Id));
        await db.SaveChangesAsync(ct);

        return new CostingCostCenterResponseModel(
            cc.Id, cc.Code, cc.Name, cc.ParentId, cc.Type.ToString(), cc.Sqft, cc.Headcount, cc.IsInventoriable);
    }
}
