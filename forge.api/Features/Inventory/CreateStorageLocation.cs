using FluentValidation;
using MediatR;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Inventory;

public record CreateStorageLocationCommand(CreateStorageLocationRequestModel Data) : IRequest<StorageLocationResponseModel>;

public class CreateStorageLocationCommandValidator : AbstractValidator<CreateStorageLocationCommand>
{
    public CreateStorageLocationCommandValidator()
    {
        RuleFor(x => x.Data.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Data.Barcode).MaximumLength(100).When(x => x.Data.Barcode is not null);
        RuleFor(x => x.Data.Description).MaximumLength(500).When(x => x.Data.Description is not null);
    }
}

public class CreateStorageLocationHandler(IInventoryRepository repo, IBarcodeService barcodeService) : IRequestHandler<CreateStorageLocationCommand, StorageLocationResponseModel>
{
    public async Task<StorageLocationResponseModel> Handle(CreateStorageLocationCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;

        if (!string.IsNullOrWhiteSpace(data.Barcode) && await repo.BarcodeExistsAsync(data.Barcode, null, cancellationToken))
            throw new InvalidOperationException($"Barcode '{data.Barcode}' already exists.");

        if (data.ParentId.HasValue)
        {
            var parent = await repo.FindLocationAsync(data.ParentId.Value, cancellationToken)
                ?? throw new KeyNotFoundException("Parent location not found.");
        }

        var location = new StorageLocation
        {
            Name = data.Name.Trim(),
            LocationType = data.LocationType,
            ParentId = data.ParentId,
            Barcode = data.Barcode?.Trim(),
            Description = data.Description?.Trim(),
        };

        await repo.AddLocationAsync(location, cancellationToken);

        await barcodeService.CreateBarcodeAsync(
            BarcodeEntityType.StorageLocation, location.Id, location.Name, cancellationToken);

        return new StorageLocationResponseModel(
            location.Id, location.Name, location.LocationType, location.ParentId,
            location.Barcode, location.Description, location.SortOrder, location.IsActive,
            location.Name, 0, []);
    }
}
