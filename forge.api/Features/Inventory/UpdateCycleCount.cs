using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Inventory;

public record UpdateCycleCountCommand(int Id, UpdateCycleCountRequestModel Data) : IRequest;

public class UpdateCycleCountCommandValidator : AbstractValidator<UpdateCycleCountCommand>
{
    public UpdateCycleCountCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
    }
}

public class UpdateCycleCountHandler(
    IInventoryRepository repo,
    IHttpContextAccessor httpContext)
    : IRequestHandler<UpdateCycleCountCommand>
{
    public async Task Handle(UpdateCycleCountCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

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
                        content.Quantity = line.ActualQuantity;

                        if (line.ActualQuantity == 0)
                        {
                            content.RemovedAt = DateTimeOffset.UtcNow;
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
                            MovedAt = DateTimeOffset.UtcNow,
                            Reason = BinMovementReason.CycleCount,
                        };

                        await repo.AddMovementAsync(movement, cancellationToken);
                    }
                }
            }
        }
        else if (data.Status == "Rejected")
        {
            cycleCount.Status = "Rejected";
        }

        await repo.SaveChangesAsync(cancellationToken);
    }
}
