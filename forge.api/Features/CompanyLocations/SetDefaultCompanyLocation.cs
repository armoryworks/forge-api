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

        // Clear existing default
        var currentDefault = await db.CompanyLocations
            .FirstOrDefaultAsync(x => x.IsDefault, ct);

        if (currentDefault != null)
            currentDefault.IsDefault = false;

        location.IsDefault = true;
        await db.SaveChangesAsync(ct);
    }
}
