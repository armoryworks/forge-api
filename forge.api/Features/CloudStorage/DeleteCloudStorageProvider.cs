using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.CloudStorage;

public record DeleteCloudStorageProviderCommand(int Id) : IRequest;

public class DeleteCloudStorageProviderHandler(AppDbContext db, IClock clock) : IRequestHandler<DeleteCloudStorageProviderCommand>
{
    public async Task Handle(DeleteCloudStorageProviderCommand request, CancellationToken ct)
    {
        var provider = await db.CloudStorageProviders.FirstOrDefaultAsync(p => p.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Cloud storage provider {request.Id} not found.");

        if (await db.EntityCloudLinks.AnyAsync(l => l.ProviderId == provider.Id, ct))
            throw new InvalidOperationException("Records are linked to folders on this provider. Deactivate it instead of deleting it.");

        provider.IsActive = false;
        provider.DeletedAt = clock.UtcNow;
        db.LogActivityAt("deleted", $"Deleted the {provider.ProviderCode} cloud storage provider", ("CloudStorageProvider", provider.Id));
        await db.SaveChangesAsync(ct);
    }
}
