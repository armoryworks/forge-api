using FluentValidation;
using MediatR;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Assets;

public record CreateAssetCommand(CreateAssetRequestModel Data) : IRequest<AssetResponseModel>;

public class CreateAssetCommandValidator : AbstractValidator<CreateAssetCommand>
{
    public CreateAssetCommandValidator()
    {
        RuleFor(x => x.Data.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Data.SerialNumber).MaximumLength(100).When(x => x.Data.SerialNumber is not null);
        RuleFor(x => x.Data.Location).MaximumLength(200).When(x => x.Data.Location is not null);

        // Phase 3 F4 — bounds on the new full-record fields. Acquisition cost
        // bounded the same as customer credit limit (0 .. 1B); above that
        // the operator is more likely to have typo'd than to actually have a
        // billion-dollar machine on the floor.
        RuleFor(x => x.Data.AcquisitionCost)
            .InclusiveBetween(0m, 1_000_000_000m)
            .When(x => x.Data.AcquisitionCost.HasValue)
            .WithMessage("Acquisition cost must be between 0 and 1,000,000,000.");

        RuleFor(x => x.Data.GlAccount)
            .MaximumLength(100)
            .When(x => x.Data.GlAccount is not null);
    }
}

public class CreateAssetHandler(IAssetRepository repo, IBarcodeService barcodeService) : IRequestHandler<CreateAssetCommand, AssetResponseModel>
{
    public async Task<AssetResponseModel> Handle(CreateAssetCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        var asset = new Asset
        {
            Name = data.Name.Trim(),
            AssetType = data.AssetType,
            Location = data.Location?.Trim(),
            Manufacturer = data.Manufacturer?.Trim(),
            Model = data.Model?.Trim(),
            SerialNumber = data.SerialNumber?.Trim(),
            Notes = data.Notes?.Trim(),
            IsCustomerOwned = data.IsCustomerOwned ?? false,
            CavityCount = data.CavityCount,
            ToolLifeExpectancy = data.ToolLifeExpectancy,
            SourceJobId = data.SourceJobId,
            SourcePartId = data.SourcePartId,
            // Phase 3 F4 — full-record fields written at create time.
            AcquisitionCost = data.AcquisitionCost,
            DepreciationMethod = data.DepreciationMethod,
            WorkCenterId = data.WorkCenterId,
            GlAccount = data.GlAccount?.Trim(),
        };

        await repo.AddAsync(asset, cancellationToken);

        var naturalId = asset.SerialNumber ?? asset.Name;
        await barcodeService.CreateBarcodeAsync(
            BarcodeEntityType.Asset, asset.Id, naturalId, cancellationToken);

        return (await repo.GetByIdAsync(asset.Id, cancellationToken))!;
    }
}
