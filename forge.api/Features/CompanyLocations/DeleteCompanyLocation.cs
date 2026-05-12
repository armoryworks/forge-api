using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Data.Context;

namespace Forge.Api.Features.CompanyLocations;

public record DeleteCompanyLocationCommand(int Id) : IRequest;

public class DeleteCompanyLocationHandler(AppDbContext db)
    : IRequestHandler<DeleteCompanyLocationCommand>
{
    public async Task Handle(DeleteCompanyLocationCommand request, CancellationToken ct)
    {
        var location = await db.CompanyLocations
            .FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Company location {request.Id} not found");

        if (location.IsDefault)
            throw new InvalidOperationException("Cannot delete the default location. Set another location as default first.");

        location.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
