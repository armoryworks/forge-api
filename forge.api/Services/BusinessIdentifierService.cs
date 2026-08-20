using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <inheritdoc />
/// <remarks>
/// "Active" is <c>EffectiveTo == null</c> (portable across providers; the DB's generated
/// <c>is_active</c> column + partial unique index are the integrity backstop). Uniqueness is checked
/// in-app for a clean error and enforced at the DB for the race.
/// </remarks>
public class BusinessIdentifierService(AppDbContext db, IClock clock) : IBusinessIdentifierService
{
    public async Task<BusinessIdentifier> IssueAsync(BusinessEntityType type, int entityId, string value, CancellationToken ct = default)
    {
        var v = Normalize(value);
        var current = await ActiveRow(type, entityId, ct);
        if (current is not null)
        {
            if (string.Equals(current.Value, v, StringComparison.Ordinal)) return current;
            return await RenameAsync(type, entityId, v, ct); // an already-issued entity that gets a new value = rename
        }

        if (await IsActiveValueTakenAsync(v, type, entityId, ct))
            throw new InvalidOperationException($"Identifier '{v}' is already in use.");

        var row = NewActiveRow(type, entityId, v, clock.UtcNow);
        db.BusinessIdentifiers.Add(row);
        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task<BusinessIdentifier> RenameAsync(BusinessEntityType type, int entityId, string newValue, CancellationToken ct = default)
    {
        var v = Normalize(newValue);
        var current = await ActiveRow(type, entityId, ct);
        if (current is not null && string.Equals(current.Value, v, StringComparison.Ordinal))
            return current; // unchanged

        if (await IsActiveValueTakenAsync(v, type, entityId, ct))
            throw new InvalidOperationException($"Identifier '{v}' is already in use.");

        var now = clock.UtcNow;
        if (current is not null)
            current.EffectiveTo = now; // close the old row — kept for history + resolution

        var row = NewActiveRow(type, entityId, v, now);
        db.BusinessIdentifiers.Add(row);
        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task<string?> GetCurrentAsync(BusinessEntityType type, int entityId, CancellationToken ct = default)
        => await db.BusinessIdentifiers
            .Where(b => b.EntityType == type && b.EntityId == entityId && b.EffectiveTo == null)
            .Select(b => b.Value)
            .FirstOrDefaultAsync(ct);

    public async Task<BusinessIdentifier?> ResolveAsync(string value, CancellationToken ct = default)
    {
        var v = Normalize(value);
        return await db.BusinessIdentifiers
            .Where(b => b.Value == v)
            .OrderByDescending(b => b.EffectiveTo == null) // active owner first
            .ThenByDescending(b => b.EffectiveFrom)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<BusinessIdentifier>> GetHistoryAsync(BusinessEntityType type, int entityId, CancellationToken ct = default)
        => await db.BusinessIdentifiers
            .Where(b => b.EntityType == type && b.EntityId == entityId)
            .OrderByDescending(b => b.EffectiveFrom)
            .ToListAsync(ct);

    public async Task<bool> IsActiveValueTakenAsync(string value, BusinessEntityType type, int entityId, CancellationToken ct = default)
    {
        var v = Normalize(value);
        return await db.BusinessIdentifiers.AnyAsync(
            b => b.Value == v && b.EffectiveTo == null && !(b.EntityType == type && b.EntityId == entityId), ct);
    }

    private Task<BusinessIdentifier?> ActiveRow(BusinessEntityType type, int entityId, CancellationToken ct)
        => db.BusinessIdentifiers.FirstOrDefaultAsync(
            b => b.EntityType == type && b.EntityId == entityId && b.EffectiveTo == null, ct);

    private static BusinessIdentifier NewActiveRow(BusinessEntityType type, int entityId, string value, DateTimeOffset from)
        => new() { EntityType = type, EntityId = entityId, Value = value, EffectiveFrom = from, EffectiveTo = null };

    private static string Normalize(string? value)
    {
        var v = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(v))
            throw new InvalidOperationException("An identifier value is required.");
        return v;
    }
}
