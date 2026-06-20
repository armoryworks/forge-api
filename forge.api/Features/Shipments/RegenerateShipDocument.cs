using MediatR;

using Forge.Api.Features.Documents;

namespace Forge.Api.Features.Shipments;

public record RegenerateShipDocumentCommand(int Id) : IRequest<byte[]>;

/// <summary>
/// Regenerates the wrapped ship document: composes a fresh copy and stores it as the next version,
/// end-dating + archiving the prior one (kept as history). Returns the new version's bytes.
/// </summary>
public class RegenerateShipDocumentHandler(ShipDocumentComposer composer, IDocumentStore documents)
    : IRequestHandler<RegenerateShipDocumentCommand, byte[]>
{
    public async Task<byte[]> Handle(RegenerateShipDocumentCommand request, CancellationToken ct)
    {
        var composed = await composer.ComposeAsync(request.Id, ct);
        await documents.StoreAsync(
            ShipDocumentComposer.Kind, composed.Primary, composed.Links,
            composed.Pdf, composed.FileName, "application/pdf", ct);
        return composed.Pdf;
    }
}
