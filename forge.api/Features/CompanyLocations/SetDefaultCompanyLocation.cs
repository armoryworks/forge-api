using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Data.Context;

namespace Forge.Api.Features.CompanyLocations;

public record SetDefaultCompanyLocationCommand(int Id) : IRequest;

public class SetDefaultCompanyLocationHandler(AppDbContext db)
    : IRequestHandler<SetDefaultCompanyLocationCommand>
{
    public async Task Handle(SetDefaultCompanyLocationCommand request, CancellationToken ct)
    {
        var location = await db.CompanyLocations
            .FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Company location {request.Id} not found");

        if (!location.IsActive)
            throw new InvalidOperationException("Cannot set an inactive location as default.");

        // Atomic default swap. The filtered unique index (is_default = true) means a
        // single batched "clear old + set new" SaveChanges can violate the constraint
        // if EF orders the set-new UPDATE before the clear-old one (BE-1 / F-12-BE-02).
        // Clear the prior default via a discrete ExecuteUpdate statement first, then set
        // the target, both inside one transaction — so the index only ever sees one true row.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.CompanyLocations
            .Where(x => x.IsDefault && x.Id != request.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, false), ct);

        location.IsDefault = true;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
