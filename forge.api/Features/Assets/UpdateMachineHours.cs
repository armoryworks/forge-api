using FluentValidation;
using MediatR;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Assets;

public record UpdateMachineHoursCommand(int AssetId, UpdateMachineHoursRequestModel Data) : IRequest<AssetResponseModel>;

public class UpdateMachineHoursValidator : AbstractValidator<UpdateMachineHoursCommand>
{
    public UpdateMachineHoursValidator()
    {
        RuleFor(x => x.AssetId).GreaterThan(0);
        RuleFor(x => x.Data.CurrentHours).GreaterThanOrEqualTo(0);
    }
}

public class UpdateMachineHoursHandler(AppDbContext db) : IRequestHandler<UpdateMachineHoursCommand, AssetResponseModel>
{
    public async Task<AssetResponseModel> Handle(UpdateMachineHoursCommand request, CancellationToken cancellationToken)
    {
        var asset = await db.Assets.FindAsync([request.AssetId], cancellationToken)
            ?? throw new KeyNotFoundException($"Asset {request.AssetId} not found.");

        asset.CurrentHours = request.Data.CurrentHours;
        await db.SaveChangesAsync(cancellationToken);

        return new AssetResponseModel(
            asset.Id, asset.Name, asset.AssetType, asset.Location, asset.Manufacturer,
            asset.Model, asset.SerialNumber, asset.Status, asset.PhotoFileId,
            asset.CurrentHours, asset.Notes,
            asset.IsCustomerOwned, asset.CavityCount, asset.ToolLifeExpectancy,
            asset.CurrentShotCount, asset.SourceJobId, asset.SourceJob?.JobNumber,
            asset.SourcePartId, asset.SourcePart?.PartNumber,
            asset.CreatedAt, asset.UpdatedAt);
    }
}
