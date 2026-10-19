using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record UpdateCycleCountCommand(int Id, UpdateCycleCountRequestModel Data) : IRequest;

public class UpdateCycleCountCommandValidator : AbstractValidator<UpdateCycleCountCommand>
{
    public UpdateCycleCountCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleForEach(x => x.Data.Lines)
            .ChildRules(line => line.RuleFor(l => l.ActualQuantity)
                .GreaterThanOrEqualTo(0).WithMessage("A counted quantity cannot be negative."))
            .When(x => x.Data.Lines != null);
    }
}

public class UpdateCycleCountHandler(
    IInventoryRepository repo,
    IHttpContextAccessor httpContext,
    IClock clock,
    AppDbContext db,
    IInventoryAdjustmentPostingService posting)
    : IRequestHandler<UpdateCycleCountCommand>
{
    public async Task Handle(UpdateCycleCountCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var now = clock.UtcNow;

        var cycleCount = await repo.FindCycleCountAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Cycle count {request.Id} not found");

        if (cycleCount.Status == "Approved")
            throw new InvalidOperationException("Cannot update an approved cycle count");

        // Update line actual quantities
        if (data.Lines != null)
        {
            foreach (var lineUpdate in data.Lines)
            {
                var line = cycleCount.Lines.FirstOrDefault(l => l.Id == lineUpdate.Id)
                    ?? throw new KeyNotFoundException($"Cycle count line {lineUpdate.Id} not found");

                line.ActualQuantity = lineUpdate.ActualQuantity;
                line.Notes = lineUpdate.Notes;
            }
        }

        if (data.Notes != null)
            cycleCount.Notes = data.Notes;

        await using var tx = data.Status == "Approved" && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var adjustments = new List<InventoryAdjustmentPosting>();

        // If approving, adjust bin contents to match actuals
        if (data.Status == "Approved")
        {
            cycleCount.Status = "Approved";

            foreach (var line in cycleCount.Lines.Where(l => l.Variance != 0))
            {
                if (line.BinContentId.HasValue)
                {
                    var content = await repo.FindBinContentWithLocationAsync(line.BinContentId.Value, cancellationToken);
                    if (content != null)
                    {
                        // S-RI1: approving a count that drops a bin below its reserved qty would
                        // inflate 'available' (= on-hand − reserved) into a phantom surplus.
                        if (content.ReservedQuantity > line.ActualQuantity)
                            throw new InvalidOperationException(
                                $"Cannot approve: counted {line.ActualQuantity} for a bin with {content.ReservedQuantity} reserved. " +
                                "Release the reservation first.");

                        content.Quantity = line.ActualQuantity;

                        if (line.ActualQuantity == 0)
                        {
                            content.RemovedAt = now;
                            content.RemovedBy = userId;
                        }

                        // Create movement record for the adjustment
                        var movement = new BinMovement
                        {
                            EntityType = line.EntityType,
                            EntityId = line.EntityId,
                            Quantity = Math.Abs(line.Variance),
                            LotNumber = content.LotNumber,
                            FromLocationId = line.Variance < 0 ? content.LocationId : null,
                            ToLocationId = line.Variance > 0 ? content.LocationId : null,
                            MovedBy = userId,
                            MovedAt = now,
                            Reason = BinMovementReason.CycleCount,
                        };

                        await repo.AddMovementAsync(movement, cancellationToken);

                        if (string.Equals(line.EntityType, "part", StringComparison.OrdinalIgnoreCase))
                            adjustments.Add(new InventoryAdjustmentPosting(
                                "CycleCountLine", line.Id, line.EntityId, line.Variance,
                                DateOnly.FromDateTime(now.UtcDateTime), userId, $"cycle count {cycleCount.Id}"));
                    }
                }
            }
        }
        else if (data.Status == "Rejected")
        {
            cycleCount.Status = "Rejected";
        }

        await repo.SaveChangesAsync(cancellationToken);

        foreach (var adjustment in adjustments)
            await posting.PostAdjustmentAsync(adjustment, cancellationToken);

        if (tx is not null)
            await tx.CommitAsync(cancellationToken);
    }
}
