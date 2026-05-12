using FluentValidation;
using MediatR;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.PriceLists;

public record CreatePriceListEntryCommand(
    int PriceListId,
    int PartId,
    decimal UnitPrice,
    int MinQuantity,
    string Currency,
    string? Notes) : IRequest<PriceListEntryResponseModel>;

public class CreatePriceListEntryValidator : AbstractValidator<CreatePriceListEntryCommand>
{
    public CreatePriceListEntryValidator()
    {
        RuleFor(x => x.PartId).GreaterThan(0);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MinQuantity).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class CreatePriceListEntryHandler(IPriceListRepository repo)
    : IRequestHandler<CreatePriceListEntryCommand, PriceListEntryResponseModel>
{
    public async Task<PriceListEntryResponseModel> Handle(
        CreatePriceListEntryCommand request, CancellationToken cancellationToken)
    {
        if (!await repo.PriceListExistsAsync(request.PriceListId, cancellationToken))
            throw new KeyNotFoundException($"Price list {request.PriceListId} not found");

        var entry = new PriceListEntry
        {
            PriceListId = request.PriceListId,
            PartId = request.PartId,
            UnitPrice = request.UnitPrice,
            MinQuantity = request.MinQuantity,
            Currency = request.Currency,
            Notes = request.Notes,
        };

        await repo.AddEntryAsync(entry, cancellationToken);
        await repo.SaveChangesAsync(cancellationToken);

        // Re-load with the part for the response model.
        var loaded = await repo.FindEntryWithPartAsync(entry.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Failed to reload entry {entry.Id} after save");

        return new PriceListEntryResponseModel(
            loaded.Id, loaded.PriceListId, loaded.PartId,
            loaded.Part.PartNumber, loaded.Part.Name,
            loaded.UnitPrice, loaded.MinQuantity,
            loaded.Currency, loaded.Notes,
            loaded.CreatedAt, loaded.UpdatedAt);
    }
}
