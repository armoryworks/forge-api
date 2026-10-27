using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Inventory;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Scanner;

public record GetScanIssueSourcesQuery(int PartId) : IRequest<List<ScanIssueSourceModel>>;

public class GetScanIssueSourcesHandler(AppDbContext db, IClock clock)
    : IRequestHandler<GetScanIssueSourcesQuery, List<ScanIssueSourceModel>>
{
    public async Task<List<ScanIssueSourceModel>> Handle(GetScanIssueSourcesQuery request, CancellationToken cancellationToken)
    {
        var rows = await db.BinContents.AsNoTracking()
            .Include(bc => bc.Location)
            .Where(bc => bc.EntityType == "part"
                && bc.EntityId == request.PartId
                && bc.RemovedAt == null
                && bc.Status != BinContentStatus.QcHold
                && bc.Quantity - bc.ReservedQuantity > 0
                && bc.Location.IsActive
                && bc.Location.DeletedAt == null)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return [];

        var shopToday = await LotExpiry.ShopTodayAsync(db, clock.UtcNow, cancellationToken);
        var lotNumbers = rows.Where(r => r.LotNumber != null).Select(r => r.LotNumber!).Distinct().ToList();
        var expiries = (await db.LotRecords.AsNoTracking()
                .Where(l => l.PartId == request.PartId && l.ExpirationDate != null && lotNumbers.Contains(l.LotNumber))
                .Select(l => new { l.LotNumber, l.ExpirationDate })
                .ToListAsync(cancellationToken))
            .GroupBy(l => l.LotNumber)
            .ToDictionary(g => g.Key, g => g.Min(l => DateOnly.FromDateTime(l.ExpirationDate!.Value.UtcDateTime)));

        var locations = await db.StorageLocations.AsNoTracking()
            .Where(l => l.DeletedAt == null)
            .Select(l => new { l.Id, l.Name, l.ParentId })
            .ToDictionaryAsync(l => l.Id, cancellationToken);

        string PathOf(StorageLocation location)
        {
            var names = new List<string> { location.Name };
            var parentId = location.ParentId;
            var guard = 0;
            while (parentId is int id && locations.TryGetValue(id, out var parent) && guard++ < 32)
            {
                names.Insert(0, parent.Name);
                parentId = parent.ParentId;
            }
            return string.Join(" / ", names);
        }

        DateOnly? ExpiryOf(BinContent row)
            => row.LotNumber is not null && expiries.TryGetValue(row.LotNumber, out var date) ? date : null;

        return rows
            .Where(r => ExpiryOf(r) is not DateOnly date || date >= shopToday)
            .OrderBy(r => ExpiryOf(r) is null)
            .ThenBy(r => ExpiryOf(r))
            .ThenBy(r => r.PlacedAt)
            .ThenBy(r => r.Id)
            .Select(r => new ScanIssueSourceModel(
                r.Id,
                r.LocationId,
                PathOf(r.Location),
                r.Location.Barcode,
                r.LotNumber,
                ExpiryOf(r),
                r.Quantity - r.ReservedQuantity))
            .ToList();
    }
}
