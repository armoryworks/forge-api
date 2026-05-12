using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Inventory;

public record CreateCycleCountCommand(CreateCycleCountRequestModel Data) : IRequest<CycleCountResponseModel>;

public class CreateCycleCountCommandValidator : AbstractValidator<CreateCycleCountCommand>
{
    public CreateCycleCountCommandValidator()
    {
        RuleFor(x => x.Data.LocationId).GreaterThan(0);
    }
}

public class CreateCycleCountHandler(
    IInventoryRepository repo,
    IHttpContextAccessor httpContext)
    : IRequestHandler<CreateCycleCountCommand, CycleCountResponseModel>
{
    public async Task<CycleCountResponseModel> Handle(
        CreateCycleCountCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var userName = httpContext.HttpContext.User.FindFirstValue(ClaimTypes.Name) ?? "Unknown";

        var location = await repo.FindLocationAsync(data.LocationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Location {data.LocationId} not found");

        // Get current bin contents at this location
        var binContents = await repo.GetBinContentsAsync(data.LocationId, cancellationToken);

        var cycleCount = new CycleCount
        {
            LocationId = data.LocationId,
            CountedById = userId,
            CountedAt = DateTimeOffset.UtcNow,
            Status = "Pending",
            Notes = data.Notes,
        };

        // Auto-populate lines from current bin contents
        foreach (var content in binContents)
        {
            cycleCount.Lines.Add(new CycleCountLine
            {
                BinContentId = content.Id,
                EntityType = content.EntityType,
                EntityId = content.EntityId,
                ExpectedQuantity = (int)content.Quantity,
                ActualQuantity = (int)content.Quantity,
            });
        }

        await repo.AddCycleCountAsync(cycleCount, cancellationToken);

        return new CycleCountResponseModel(
            cycleCount.Id,
            cycleCount.LocationId,
            location.Name,
            cycleCount.CountedById,
            userName,
            cycleCount.CountedAt,
            cycleCount.Status,
            cycleCount.Notes,
            cycleCount.Lines.Select(l =>
            {
                var matchingContent = binContents.FirstOrDefault(c => c.Id == l.BinContentId);
                return new CycleCountLineResponseModel(
                    l.Id,
                    l.BinContentId,
                    l.EntityType,
                    l.EntityId,
                    matchingContent?.EntityName ?? $"{l.EntityType}:{l.EntityId}",
                    l.ExpectedQuantity,
                    l.ActualQuantity,
                    l.Variance,
                    l.Notes);
            }).ToList(),
            cycleCount.CreatedAt);
    }
}
