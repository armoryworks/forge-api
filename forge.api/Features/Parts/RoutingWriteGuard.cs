using System.Globalization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Parts;

/// <summary>
/// The lock and stale-version check every routing edit shares with the whole-routing save: one writer at a time
/// per part (the "parts.routing" advisory lock), and a version from an earlier read that no longer matches the
/// routing is refused with <see cref="RoutingGraphStaleException"/>, so a list reorder or a link edit cannot
/// silently overwrite a change saved from the flowchart or another list.
/// </summary>
public static class RoutingWriteGuard
{
    public static async Task<IDbContextTransaction?> BeginAsync(AppDbContext? db, int partId, CancellationToken ct)
    {
        if (db is null)
            return null;

        var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        await db.AcquireAdvisoryLockAsync("parts.routing", partId.ToString(CultureInfo.InvariantCulture), ct);
        return tx;
    }

    public static async Task EnsureCurrentAsync(IPartRepository repo, int partId, string? sentVersion, CancellationToken ct)
    {
        if (sentVersion is not { Length: > 0 })
            return;

        var version = RoutingGraphVersion.Of(await repo.GetOperationsAsync(partId, ct));
        if (sentVersion != version)
            throw new RoutingGraphStaleException(version);
    }
}
