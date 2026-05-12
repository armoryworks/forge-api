using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.PriceLists;

public record GetPriceListByIdQuery(int Id) : IRequest<PriceListResponseModel>;

public class GetPriceListByIdHandler(IPriceListRepository repo)
    : IRequestHandler<GetPriceListByIdQuery, PriceListResponseModel>
{
    public async Task<PriceListResponseModel> Handle(GetPriceListByIdQuery request, CancellationToken cancellationToken)
    {
        var pl = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Price list {request.Id} not found");

        return new PriceListResponseModel(
            pl.Id, pl.Name, pl.Description, pl.CustomerId,
            pl.Customer?.Name, pl.IsDefault, pl.IsActive,
            pl.EffectiveFrom, pl.EffectiveTo,
            pl.Entries.OrderBy(e => e.Part.PartNumber).ThenBy(e => e.MinQuantity)
                .Select(e => new PriceListEntryResponseModel(
                    e.Id, e.PriceListId, e.PartId, e.Part.PartNumber, e.Part.Name,
                    e.UnitPrice, e.MinQuantity, e.Currency, e.Notes,
                    e.CreatedAt, e.UpdatedAt)).ToList(),
            pl.CreatedAt, pl.UpdatedAt);
    }
}
