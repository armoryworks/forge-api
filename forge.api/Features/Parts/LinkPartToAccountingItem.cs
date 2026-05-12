using MediatR;

using Forge.Core.Interfaces;

namespace Forge.Api.Features.Parts;

public record LinkPartToAccountingItemCommand(
    int PartId,
    string ExternalId,
    string ExternalRef) : IRequest;

public class LinkPartToAccountingItemHandler(
    IPartRepository partRepository,
    IAccountingProviderFactory providerFactory,
    ILogger<LinkPartToAccountingItemHandler> logger) : IRequestHandler<LinkPartToAccountingItemCommand>
{
    public async Task Handle(LinkPartToAccountingItemCommand request, CancellationToken ct)
    {
        var part = await partRepository.FindAsync(request.PartId, ct)
            ?? throw new KeyNotFoundException($"Part {request.PartId} not found");

        var provider = await providerFactory.GetActiveProviderAsync(ct);
        var providerId = provider?.ProviderId;

        part.ExternalId = request.ExternalId;
        part.ExternalRef = request.ExternalRef;
        part.Provider = providerId;

        await partRepository.SaveChangesAsync(ct);

        logger.LogInformation(
            "Linked Part {PartId} to accounting item {ExternalId} ({ExternalRef}) via {Provider}",
            request.PartId, request.ExternalId, request.ExternalRef, providerId ?? "none");
    }
}
