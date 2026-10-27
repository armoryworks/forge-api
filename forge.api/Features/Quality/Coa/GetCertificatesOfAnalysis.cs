using MediatR;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality.Coa;

/// <summary>Certificates of analysis for a lot, a shipment or a customer (any combination), newest first.</summary>
public record GetCertificatesOfAnalysisQuery(int? LotId, int? ShipmentId, int? CustomerId)
    : IRequest<List<CertificateOfAnalysisResponseModel>>;

public class GetCertificatesOfAnalysisHandler(AppDbContext db)
    : IRequestHandler<GetCertificatesOfAnalysisQuery, List<CertificateOfAnalysisResponseModel>>
{
    public Task<List<CertificateOfAnalysisResponseModel>> Handle(
        GetCertificatesOfAnalysisQuery request, CancellationToken cancellationToken)
    {
        var query = db.CertificatesOfAnalysis.AsQueryable();
        if (request.LotId is int lotId)
            query = query.Where(c => c.LotRecordId == lotId);
        if (request.ShipmentId is int shipmentId)
            query = query.Where(c => c.ShipmentId == shipmentId);
        if (request.CustomerId is int customerId)
            query = query.Where(c => c.CustomerId == customerId);
        return CertificateOfAnalysisMapping.ListAsync(db, query, cancellationToken);
    }
}
