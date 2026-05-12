using MediatR;
using Forge.Data.Context;

namespace Forge.Api.Features.SalesTax;

public record DeleteSalesTaxRateCommand(int Id) : IRequest;

public class DeleteSalesTaxRateHandler(AppDbContext db) : IRequestHandler<DeleteSalesTaxRateCommand>
{
    public async Task Handle(DeleteSalesTaxRateCommand request, CancellationToken cancellationToken)
    {
        var rate = await db.SalesTaxRates.FindAsync([request.Id], cancellationToken)
            ?? throw new KeyNotFoundException($"Sales tax rate {request.Id} not found.");

        rate.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
