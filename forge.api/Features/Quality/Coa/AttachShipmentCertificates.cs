using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Core.Models;

namespace Forge.Api.Features.Quality.Coa;

/// <summary>Attaches certificates to a shipment as the signed-in user, or as <paramref name="UserId"/> when a background reaction issues them.</summary>
public record AttachShipmentCertificatesCommand(int ShipmentId, AttachShipmentCertificatesRequestModel Data, int? UserId = null)
    : IRequest<ShipmentCertificatesResponseModel>;

public class AttachShipmentCertificatesHandler(
    CertificateOfAnalysisIssuer issuer,
    IHttpContextAccessor httpContext) : IRequestHandler<AttachShipmentCertificatesCommand, ShipmentCertificatesResponseModel>
{
    public Task<ShipmentCertificatesResponseModel> Handle(
        AttachShipmentCertificatesCommand request, CancellationToken cancellationToken)
    {
        var userId = request.UserId
            ?? (int.TryParse(httpContext.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : null);
        return issuer.AttachToShipmentAsync(
            request.ShipmentId, request.Data.LotId, request.Data.Language, userId, cancellationToken);
    }
}
