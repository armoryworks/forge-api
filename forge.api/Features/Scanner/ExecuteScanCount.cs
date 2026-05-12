using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

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

        // Get current recorded quantity
        var binContent = await db.BinContents
            .Where(bc => bc.EntityType == "part"
                && bc.EntityId == data.PartId
                && bc.LocationId == data.LocationId
                && bc.RemovedAt == null)
            .FirstOrDefaultAsync(cancellationToken);

        var recordedQty = binContent?.Quantity ?? 0;
        var delta = data.ActualCount - recordedQty;

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
            // Update or create bin content
            if (binContent != null)
            {
                binContent.Quantity = data.ActualCount;
                if (data.ActualCount == 0)
                {
                    binContent.RemovedAt = now;
                    binContent.RemovedBy = userId;
                }
            }
            else if (data.ActualCount > 0)
            {
                binContent = new BinContent
                {
                    LocationId = data.LocationId,
                    EntityType = "part",
                    EntityId = data.PartId,
                    Quantity = data.ActualCount,
                    PlacedBy = userId,
                    PlacedAt = now,
                };
                db.BinContents.Add(binContent);
            }

            await db.SaveChangesAsync(cancellationToken);

            // Create adjustment movement
            var movement = new BinMovement
            {
                EntityType = "part",
                EntityId = data.PartId,
                Quantity = Math.Abs(delta),
                FromLocationId = delta < 0 ? data.LocationId : null,
                ToLocationId = delta > 0 ? data.LocationId : null,
                MovedBy = userId,
                MovedAt = now,
                Reason = BinMovementReason.ScanCycleCount,
                ScanActionLogId = scanLog.Id,
            };
            db.BinMovements.Add(movement);

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
