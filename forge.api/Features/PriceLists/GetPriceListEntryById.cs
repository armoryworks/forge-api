using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.PriceLists;

public record GetPriceListEntryByIdQuery(int Id) : IRequest<PriceListEntryResponseModel>;

public class GetPriceListEntryByIdHandler(IPriceListRepository repo)
    : IRequestHandler<GetPriceListEntryByIdQuery, PriceListEntryResponseModel>
{
    public async Task<PriceListEntryResponseModel> Handle(
        GetPriceListEntryByIdQuery request, CancellationToken cancellationToken)
    {
        var entry = await repo.FindEntryWithPartAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Price list entry {request.Id} not found");

        return new PriceListEntryResponseModel(
            entry.Id, entry.PriceListId, entry.PartId,
            entry.Part.PartNumber, entry.Part.Name,
            entry.UnitPrice, entry.MinQuantity,
            entry.Currency, entry.Notes,
            entry.CreatedAt, entry.UpdatedAt);
    }
}
