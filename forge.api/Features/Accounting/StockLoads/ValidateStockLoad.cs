using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting.StockLoads;

/// <summary>The stock-load dry run: errors, warnings and totals per inventory account, with no writes.</summary>
[RequiresAnyCapability("CAP-ACCT-MIGRATION", "CAP-ACCT-FULLGL")]
public record ValidateStockLoadQuery(int Id) : IRequest<StockLoadValidationResult>;

public class ValidateStockLoadHandler(AppDbContext db, IStockLoadService stockLoads)
    : IRequestHandler<ValidateStockLoadQuery, StockLoadValidationResult>
{
    public async Task<StockLoadValidationResult> Handle(ValidateStockLoadQuery request, CancellationToken ct)
    {
        var load = await db.InventoryStockLoads.AsNoTracking().Include(l => l.Lines)
            .FirstOrDefaultAsync(l => l.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Stock load {request.Id} not found.");
        return await stockLoads.ValidateAsync(load, ct);
    }
}
