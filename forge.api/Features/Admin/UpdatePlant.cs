using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record UpdatePlantCommand(int Id, UpdatePlantRequestModel Request) : IRequest;

public class UpdatePlantHandler(AppDbContext db) : IRequestHandler<UpdatePlantCommand>
{
    public async Task Handle(UpdatePlantCommand command, CancellationToken cancellationToken)
    {
        var plant = await db.Plants.FindAsync(new object[] { command.Id }, cancellationToken)
            ?? throw new KeyNotFoundException($"Plant {command.Id} not found");

        var request = command.Request;

        if (request.IsDefault && !plant.IsDefault)
        {
            var existingDefault = await db.Plants
                .Where(p => p.IsDefault && p.Id != command.Id)
                .ToListAsync(cancellationToken);

            foreach (var p in existingDefault)
                p.IsDefault = false;
        }

        plant.Code = request.Code;
        plant.Name = request.Name;
        plant.CompanyLocationId = request.CompanyLocationId;
        plant.TimeZone = request.TimeZone;
        plant.CurrencyCode = request.CurrencyCode;
        plant.IsActive = request.IsActive;
        plant.IsDefault = request.IsDefault;

        await db.SaveChangesAsync(cancellationToken);
    }
}
