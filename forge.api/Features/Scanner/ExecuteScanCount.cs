using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Inventory;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Scanner;

public record ExecuteScanCountCommand(ScanCountRequestModel Data) : IRequest<int>;

public class ExecuteScanCountCommandValidator : AbstractValidator<ExecuteScanCountCommand>
{
    public ExecuteScanCountCommandValidator()
    {
        RuleFor(x => x.Data.PartId).GreaterThan(0);
        RuleFor(x => x.Data.LocationId).GreaterThan(0);
        RuleFor(x => x.Data.ActualCount).GreaterThanOrEqualTo(0);
    }
}

public class ExecuteScanCountHandler(
    AppDbContext db,
    IClock clock,
    IHttpContextAccessor httpContext)
    : IRequestHandler<ExecuteScanCountCommand, int>
{
    private const decimal VarianceThresholdPercent = 0.10m;

    public async Task<int> Handle(ExecuteScanCountCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var now = clock.UtcNow;

        // Validate part exists
        var part = await db.Parts.AsNoTracking()
            .Where(p => p.Id == data.PartId)
            .Select(p => new { p.Id, p.PartNumber })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Part {data.PartId} not found");

        // Validate location exists
        var locationExists = await db.StorageLocations.AsNoTracking()
            .AnyAsync(sl => sl.Id == data.LocationId, cancellationToken);
        if (!locationExists)
            throw new KeyNotFoundException($"Location {data.LocationId} not found");

        var rows = await ScanBinStock.ActiveRowsAsync(db, data.PartId, data.LocationId, cancellationToken);
        var recordedQty = rows.Sum(r => r.Quantity);
        var delta = data.ActualCount - recordedQty;

        var reserved = rows.Sum(r => r.ReservedQuantity);
        if (delta < 0 && data.ActualCount < reserved)
            throw new InvalidOperationException(
                $"Cannot count {data.ActualCount}: {reserved} unit(s) here are reserved. Release the reservation first.");

        // Create scan action log
        var scanLog = new ScanActionLog
        {
            UserId = userId,
            ActionType = ScanActionType.CycleCount,
            PartId = data.PartId,
            PartNumber = part.PartNumber,
            FromLocationId = data.LocationId,
            ToLocationId = data.LocationId,
            Quantity = data.ActualCount,
        };
        db.ScanActionLogs.Add(scanLog);

        if (delta != 0)
        {
            var adjustments = new List<(string? LotNumber, decimal Delta)>();
            if (delta < 0)
            {
                foreach (var (row, taken) in BinContentDrawDown.Take(rows, -delta, userId, now))
                    adjustments.Add((row.LotNumber, -taken));
            }
            else
            {
                var target = rows.FirstOrDefault(r => r.LotNumber is null)
                    ?? rows.OrderByDescending(r => r.PlacedAt).ThenByDescending(r => r.Id).FirstOrDefault();
                if (target is not null)
                    target.Quantity += delta;
                else
                    await ScanBinStock.AddAsync(db, data.PartId, data.LocationId, null, delta, userId, now, cancellationToken);
                adjustments.Add((target?.LotNumber, delta));
            }

            await db.SaveChangesAsync(cancellationToken);

            foreach (var (lotNumber, change) in adjustments)
            {
                db.BinMovements.Add(new BinMovement
                {
                    EntityType = "part",
                    EntityId = data.PartId,
                    Quantity = Math.Abs(change),
                    LotNumber = lotNumber,
                    FromLocationId = change < 0 ? data.LocationId : null,
                    ToLocationId = change > 0 ? data.LocationId : null,
                    MovedBy = userId,
                    MovedAt = now,
                    Reason = BinMovementReason.ScanCycleCount,
                    ScanActionLogId = scanLog.Id,
                });
            }

            // If variance exceeds threshold, notify all managers/admins
            if (recordedQty > 0)
            {
                var variancePercent = Math.Abs(delta) / recordedQty;
                if (variancePercent > VarianceThresholdPercent)
                {
                    var managerUserIds = await db.UserRoles
                        .Join(db.Roles.Where(r => r.Name == "Admin" || r.Name == "Manager"),
                            ur => ur.RoleId, r => r.Id, (ur, _) => ur.UserId)
                        .Distinct()
                        .ToListAsync(cancellationToken);

                    foreach (var managerId in managerUserIds)
                    {
                        db.Notifications.Add(new Notification
                        {
                            UserId = managerId,
                            Title = "Cycle Count Variance",
                            Message = $"Part {part.PartNumber}: expected {recordedQty}, counted {data.ActualCount} (variance {variancePercent:P0})",
                            Severity = "warning",
                            Type = "inventory",
                            Source = "scanner",
                            EntityType = "Part",
                            EntityId = data.PartId,
                        });
                    }
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return scanLog.Id;
    }
}
