using FluentValidation;
using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

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
    }
}

public class UpdateAssetHandler(IAssetRepository repo) : IRequestHandler<UpdateAssetCommand, AssetResponseModel>
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
        if (data.Status.HasValue) asset.Status = data.Status.Value;
        if (data.CurrentHours.HasValue) asset.CurrentHours = data.CurrentHours.Value;
        if (data.Notes is not null) asset.Notes = data.Notes.Trim();
        if (data.IsCustomerOwned.HasValue) asset.IsCustomerOwned = data.IsCustomerOwned.Value;
        if (data.CavityCount.HasValue) asset.CavityCount = data.CavityCount.Value;
        if (data.ToolLifeExpectancy.HasValue) asset.ToolLifeExpectancy = data.ToolLifeExpectancy.Value;
        if (data.CurrentShotCount.HasValue) asset.CurrentShotCount = data.CurrentShotCount.Value;
        if (data.SourceJobId.HasValue) asset.SourceJobId = data.SourceJobId.Value;
        if (data.SourcePartId.HasValue) asset.SourcePartId = data.SourcePartId.Value;

        await repo.SaveChangesAsync(cancellationToken);
        return (await repo.GetByIdAsync(asset.Id, cancellationToken))!;
    }
}
