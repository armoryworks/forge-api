using MediatR;

using Forge.Core.Interfaces;

namespace Forge.Api.Features.Parts;

public record UnlinkPartFromAccountingItemCommand(int PartId) : IRequest;

public class UnlinkPartFromAccountingItemHandler(
    IPartRepository partRepository,
    ILogger<UnlinkPartFromAccountingItemHandler> logger) : IRequestHandler<UnlinkPartFromAccountingItemCommand>
{
    public async Task Handle(UnlinkPartFromAccountingItemCommand request, CancellationToken ct)
    {
        var part = await partRepository.FindAsync(request.PartId, ct)
            ?? throw new KeyNotFoundException($"Part {request.PartId} not found");

        part.ExternalId = null;
        part.ExternalRef = null;
        part.Provider = null;

        await partRepository.SaveChangesAsync(ct);

        logger.LogInformation("Unlinked Part {PartId} from accounting item", request.PartId);
    }
}
