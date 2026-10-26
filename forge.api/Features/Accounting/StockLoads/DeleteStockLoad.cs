using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting.StockLoads;

/// <summary>Soft-deletes a draft stock load. A posted load is voided instead.</summary>
[RequiresAnyCapability("CAP-ACCT-MIGRATION", "CAP-ACCT-FULLGL")]
public record DeleteStockLoadCommand(int Id) : IRequest;

public class DeleteStockLoadHandler(AppDbContext db, IClock clock) : IRequestHandler<DeleteStockLoadCommand>
{
    public async Task Handle(DeleteStockLoadCommand request, CancellationToken ct)
    {
        var load = await db.InventoryStockLoads.FirstOrDefaultAsync(l => l.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Stock load {request.Id} not found.");
        if (load.Status != StockLoadStatus.Draft)
            throw new InvalidOperationException($"Stock load {load.LoadNumber} is {load.Status}; only a draft can be deleted. Void a posted load instead.");

        load.DeletedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
