using FluentValidation;
using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Inventory;

// S2a: created locations were not editable (no PUT). This adds the update path so a
// location can be renamed / re-typed / re-parented, mirroring CreateStorageLocation.
public record UpdateStorageLocationCommand(int Id, UpdateStorageLocationRequestModel Data)
    : IRequest<StorageLocationResponseModel>;

public class UpdateStorageLocationCommandValidator : AbstractValidator<UpdateStorageLocationCommand>
{
    public UpdateStorageLocationCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Data.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Data.Barcode).MaximumLength(100).When(x => x.Data.Barcode is not null);
        RuleFor(x => x.Data.Description).MaximumLength(500).When(x => x.Data.Description is not null);
    }
}

public class UpdateStorageLocationHandler(IInventoryRepository repo)
    : IRequestHandler<UpdateStorageLocationCommand, StorageLocationResponseModel>
{
    public async Task<StorageLocationResponseModel> Handle(
        UpdateStorageLocationCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;

        var location = await repo.FindLocationAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Storage location {request.Id} not found");

        if (!string.IsNullOrWhiteSpace(data.Barcode)
            && await repo.BarcodeExistsAsync(data.Barcode, request.Id, cancellationToken))
            throw new InvalidOperationException($"Barcode '{data.Barcode}' already exists.");

        if (data.ParentId.HasValue)
        {
            if (data.ParentId.Value == request.Id)
                throw new InvalidOperationException("A location cannot be its own parent.");
            _ = await repo.FindLocationAsync(data.ParentId.Value, cancellationToken)
                ?? throw new KeyNotFoundException("Parent location not found.");
        }

        location.Name = data.Name.Trim();
        location.LocationType = data.LocationType;
        location.ParentId = data.ParentId;
        location.Barcode = data.Barcode?.Trim();
        location.Description = data.Description?.Trim();

        await repo.SaveChangesAsync(cancellationToken);

        return new StorageLocationResponseModel(
            location.Id, location.Name, location.LocationType, location.ParentId,
            location.Barcode, location.Description, location.SortOrder, location.IsActive,
            location.Name, 0, []);
    }
}
