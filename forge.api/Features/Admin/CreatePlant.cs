using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record CreatePlantCommand(CreatePlantRequestModel Request) : IRequest<PlantResponseModel>;

public class CreatePlantHandler(AppDbContext db) : IRequestHandler<CreatePlantCommand, PlantResponseModel>
{
    public async Task<PlantResponseModel> Handle(CreatePlantCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        var location = await db.CompanyLocations.FindAsync(new object[] { request.CompanyLocationId }, cancellationToken)
            ?? throw new KeyNotFoundException($"Company location {request.CompanyLocationId} not found");

        if (request.IsDefault)
        {
            var existingDefault = await db.Plants
                .Where(p => p.IsDefault)
                .ToListAsync(cancellationToken);

            foreach (var p in existingDefault)
                p.IsDefault = false;
        }

        var plant = new Plant
        {
            Code = request.Code,
            Name = request.Name,
            CompanyLocationId = request.CompanyLocationId,
            TimeZone = request.TimeZone,
            CurrencyCode = request.CurrencyCode,
            IsDefault = request.IsDefault,
        };

        db.Plants.Add(plant);
        await db.SaveChangesAsync(cancellationToken);

        return new PlantResponseModel(
            plant.Id, plant.Code, plant.Name, plant.CompanyLocationId,
            location.Name, plant.TimeZone, plant.CurrencyCode,
            plant.IsActive, plant.IsDefault,
            plant.CreatedAt, plant.UpdatedAt);
    }
}
