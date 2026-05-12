using MediatR;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Vendors;

public record DeleteVendorCommand(int Id) : IRequest;

public class DeleteVendorHandler(IVendorRepository repo)
    : IRequestHandler<DeleteVendorCommand>
{
    public async Task Handle(DeleteVendorCommand request, CancellationToken cancellationToken)
    {
        var vendor = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Vendor {request.Id} not found");

        vendor.DeletedAt = DateTimeOffset.UtcNow;
        await repo.SaveChangesAsync(cancellationToken);
    }
}
