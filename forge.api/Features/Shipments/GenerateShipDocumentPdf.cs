using MediatR;

using Forge.Api.Features.Documents;

namespace Forge.Api.Features.Shipments;

public record GenerateShipDocumentPdfQuery(int Id) : IRequest<byte[]>;

/// <summary>
/// Returns the current (active) version of the wrapped ship document for a shipment. If none is stored
/// yet, composes it, stores it as version 1 (linked to the shipment, sales order, jobs, invoice), and
/// returns that. Use the regenerate command to supersede it with a fresh version.
/// </summary>
public class GenerateShipDocumentPdfHandler(ShipDocumentComposer composer, IDocumentStore documents)
    : IRequestHandler<GenerateShipDocumentPdfQuery, byte[]>
{
    public async Task<byte[]> Handle(GenerateShipDocumentPdfQuery request, CancellationToken ct)
    {
        var primary = new DocumentLinkTarget("Shipment", request.Id);

        var existing = await documents.ReadCurrentAsync(ShipDocumentComposer.Kind, primary, ct);
        if (existing is not null) return existing;

        var composed = await composer.ComposeAsync(request.Id, ct);
        await documents.StoreAsync(
            ShipDocumentComposer.Kind, composed.Primary, composed.Links,
            composed.Pdf, composed.FileName, "application/pdf", ct);
        return composed.Pdf;
    }
}
