using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.TariffRates;

/// <summary>
/// Bought-parts effort PR4 — soft-deletes a TariffRate. The global query
/// filter on <c>BaseEntity.DeletedAt</c> hides the row from subsequent
/// reads + the resolver. Hard-delete isn't appropriate for an audited
/// tariff history; if the wrong rate was entered, admin should add a
/// corrected supersession (new row, prior EffectiveTo set) rather than
/// erasing the bad one.
/// </summary>
public record DeleteTariffRateCommand(int Id) : IRequest;

public class DeleteTariffRateHandler(AppDbContext db) : IRequestHandler<DeleteTariffRateCommand>
{
    public async Task Handle(DeleteTariffRateCommand request, CancellationToken ct)
    {
        var entity = await db.TariffRates.FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"TariffRate {request.Id} not found");
        entity.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
