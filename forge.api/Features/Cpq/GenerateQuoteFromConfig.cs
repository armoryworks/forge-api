using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Cpq;

public record GenerateQuoteFromConfigCommand(int ConfigurationId, GenerateQuoteFromConfigRequestModel Request) : IRequest<int>;

public class GenerateQuoteFromConfigHandler(AppDbContext db, ICpqService cpqService) : IRequestHandler<GenerateQuoteFromConfigCommand, int>
{
    public async Task<int> Handle(GenerateQuoteFromConfigCommand command, CancellationToken cancellationToken)
    {
        var config = await db.ProductConfigurations
            .FirstOrDefaultAsync(c => c.Id == command.ConfigurationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Configuration {command.ConfigurationId} not found");

        if (config.QuoteId.HasValue)
            throw new InvalidOperationException("Configuration already has a linked quote");

        var quote = await cpqService.GenerateQuoteFromConfigurationAsync(
            command.ConfigurationId, command.Request.CustomerId, cancellationToken);

        config.QuoteId = quote.Id;
        config.Status = ConfigurationStatus.Quoted;
        await db.SaveChangesAsync(cancellationToken);

        return quote.Id;
    }
}
