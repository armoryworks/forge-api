using FluentValidation;
using MediatR;
using Forge.Core.Models;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

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

public class UpdateMachineHoursHandler(AppDbContext db, IClock clock) : IRequestHandler<UpdateMachineHoursCommand, AssetResponseModel>
{
    public async Task<AssetResponseModel> Handle(UpdateMachineHoursCommand request, CancellationToken cancellationToken)
    {
        var asset = await db.Assets.FindAsync([request.AssetId], cancellationToken)
            ?? throw new KeyNotFoundException($"Asset {request.AssetId} not found.");

        if (request.Data.CurrentHours < asset.CurrentHours)
            throw new InvalidOperationException(
                $"The meter reading {request.Data.CurrentHours:0.##} h is below the last recorded reading of {asset.CurrentHours:0.##} h.");

        var readingAt = request.Data.ReadingAt ?? clock.UtcNow;
        var previous = asset.CurrentHours;
        asset.CurrentHours = request.Data.CurrentHours;
        db.LogActivityAt("hours-recorded",
            $"Meter reading {request.Data.CurrentHours:0.##} h recorded for {readingAt:yyyy-MM-dd} (was {previous:0.##} h)",
            ("Asset", asset.Id));
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
