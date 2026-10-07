using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

internal static class ReceivingInspectionLock
{
    public static async Task LockAsync(AppDbContext db, ReceivingRecord record, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql())
            return;

        await db.Database.ExecuteSqlRawAsync(
            "SELECT id FROM receiving_records WHERE id = {0} FOR UPDATE", [record.Id], ct);
        await db.Entry(record).ReloadAsync(ct);
    }
}
