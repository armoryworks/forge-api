using Microsoft.EntityFrameworkCore;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Capabilities;

/// <summary>
/// Phase 4 Phase-A — singleton in-memory snapshot of capability state.
/// </summary>
public class CapabilitySnapshotProvider(IServiceScopeFactory scopeFactory, IClock clock) : ICapabilitySnapshotProvider
{
    private CapabilitySnapshot _current = CapabilitySnapshot.Empty;

    public CapabilitySnapshot Current => Volatile.Read(ref _current);

    public bool IsEnabled(string code) => Current.IsEnabled(code);

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rows = await db.Capabilities
            .AsNoTracking()
            .Select(c => new { c.Code, c.Enabled })
            .ToListAsync(ct);

        var dict = rows.ToDictionary(r => r.Code, r => r.Enabled, StringComparer.Ordinal);
        var snap = new CapabilitySnapshot(dict, clock.UtcNow);
        Volatile.Write(ref _current, snap);
    }
}
