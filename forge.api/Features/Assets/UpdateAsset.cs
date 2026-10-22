using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Validation;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Assets;

public record UpdateAssetCommand(int Id, UpdateAssetRequestModel Data) : IRequest<AssetResponseModel>;

public class UpdateAssetCommandValidator : AbstractValidator<UpdateAssetCommand>
{
    public UpdateAssetCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Data.Name).MaximumLength(200).When(x => x.Data.Name is not null);
        RuleFor(x => x.Data.SerialNumber).MaximumLength(100).When(x => x.Data.SerialNumber is not null);
        RuleFor(x => x.Data.Location).MaximumLength(200).When(x => x.Data.Location is not null);
        RuleFor(x => x.Data.AcquisitionCost)
            .InclusiveBetween(0m, 1_000_000_000m)
            .WithMessage("Acquisition cost must be between 0 and 1,000,000,000.")
            .FitsNumeric(18, 2)
            .When(x => x.Data.AcquisitionCost.HasValue);
        RuleFor(x => x.Data.DepreciationMethod).IsInEnum().When(x => x.Data.DepreciationMethod.HasValue);
        RuleFor(x => x.Data.WorkCenterId).GreaterThan(0).When(x => x.Data.WorkCenterId.HasValue);
        RuleFor(x => x.Data.GlAccount).MaximumLength(100).When(x => x.Data.GlAccount is not null);
    }
}

public class UpdateAssetHandler(IAssetRepository repo, AppDbContext db) : IRequestHandler<UpdateAssetCommand, AssetResponseModel>
{
    public async Task<AssetResponseModel> Handle(UpdateAssetCommand request, CancellationToken cancellationToken)
    {
        var asset = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Asset not found.");

        var data = request.Data;

        if (data.Name is not null) asset.Name = data.Name.Trim();
        if (data.AssetType.HasValue) asset.AssetType = data.AssetType.Value;
        if (data.Location is not null) asset.Location = data.Location.Trim();
        if (data.Manufacturer is not null) asset.Manufacturer = data.Manufacturer.Trim();
        if (data.Model is not null) asset.Model = data.Model.Trim();
        if (data.SerialNumber is not null) asset.SerialNumber = data.SerialNumber.Trim();
        if (data.Status.HasValue && data.Status.Value != asset.Status)
        {
            // AS-03: Retired is terminal. An asset is decommissioned at end-of-life;
            // bringing it back to Active/Maintenance/OutOfService would silently revive
            // a disposed asset (and its history). Other transitions stay permissive.
            if (asset.Status == AssetStatus.Retired)
                throw new InvalidOperationException(
                    "A retired asset cannot change status — retirement is final.");

            asset.Status = data.Status.Value;
        }
        if (data.CurrentHours.HasValue) asset.CurrentHours = data.CurrentHours.Value;
        if (data.Notes is not null) asset.Notes = data.Notes.Trim();
        if (data.IsCustomerOwned.HasValue) asset.IsCustomerOwned = data.IsCustomerOwned.Value;
        if (data.CavityCount.HasValue) asset.CavityCount = data.CavityCount.Value;
        if (data.ToolLifeExpectancy.HasValue) asset.ToolLifeExpectancy = data.ToolLifeExpectancy.Value;
        if (data.CurrentShotCount.HasValue) asset.CurrentShotCount = data.CurrentShotCount.Value;
        if (data.SourceJobId.HasValue) asset.SourceJobId = data.SourceJobId.Value;
        if (data.SourcePartId.HasValue) asset.SourcePartId = data.SourcePartId.Value;
        if (data.AcquisitionCost.HasValue) asset.AcquisitionCost = data.AcquisitionCost.Value;
        if (data.DepreciationMethod.HasValue) asset.DepreciationMethod = data.DepreciationMethod.Value;
        if (data.WorkCenterId is int workCenterId && workCenterId != asset.WorkCenterId)
        {
            if (!await db.WorkCenters.AnyAsync(w => w.Id == workCenterId, cancellationToken))
                throw new ValidationException([new ValidationFailure("workCenterId", $"Work center {workCenterId} not found.")]);
            asset.WorkCenterId = workCenterId;
        }
        if (data.GlAccount is not null)
        {
            var glAccount = string.IsNullOrWhiteSpace(data.GlAccount) ? null : data.GlAccount.Trim();
            if (glAccount != asset.GlAccount)
            {
                var depreciationPosted = await db.DepreciationEntries.AnyAsync(
                    e => db.FixedAssets.Any(f => f.Id == e.FixedAssetId && f.LinkedAssetId == asset.Id),
                    cancellationToken);
                if (depreciationPosted)
                    throw new ValidationException([new ValidationFailure("glAccount",
                        "The GL account cannot change after depreciation has posted for this asset.")]);
                asset.GlAccount = glAccount;
            }
        }

        await repo.SaveChangesAsync(cancellationToken);
        return (await repo.GetByIdAsync(asset.Id, cancellationToken))!;
    }
}
