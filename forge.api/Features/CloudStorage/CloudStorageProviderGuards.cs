using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Data.Context;

namespace Forge.Api.Features.CloudStorage;

public static class CloudStorageProviderGuards
{
    public static void EnsureCapability(string code, ICapabilitySnapshotProvider capabilities)
    {
        if (CloudStorageProviderCodes.CapabilityByCode.TryGetValue(code, out var capability)
            && capability is not null
            && !capabilities.IsEnabled(capability))
        {
            throw new CapabilityDisabledException(capability);
        }
    }

    public static async Task EnsureNoOtherActiveAsync(AppDbContext db, string code, int? exceptId, CancellationToken ct)
    {
        var clash = await db.CloudStorageProviders
            .AnyAsync(p => p.ProviderCode == code && p.IsActive && (exceptId == null || p.Id != exceptId), ct);
        if (clash)
            throw new InvalidOperationException($"An active '{code}' provider already exists. Deactivate it first.");
    }
}
