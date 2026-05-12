using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Cpq;

public record GeneratePartFromConfigCommand(int ConfigurationId) : IRequest<int>;

public class GeneratePartFromConfigHandler(AppDbContext db, ICpqService cpqService) : IRequestHandler<GeneratePartFromConfigCommand, int>
{
    public async Task<int> Handle(GeneratePartFromConfigCommand command, CancellationToken cancellationToken)
    {
        var config = await db.ProductConfigurations
            .FirstOrDefaultAsync(c => c.Id == command.ConfigurationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Configuration {command.ConfigurationId} not found");

        if (config.PartId.HasValue)
            throw new InvalidOperationException("Configuration already has a linked part");

        var part = await cpqService.GeneratePartFromConfigurationAsync(
            command.ConfigurationId, cancellationToken);

        config.PartId = part.Id;
        await db.SaveChangesAsync(cancellationToken);

        return part.Id;
    }
}
