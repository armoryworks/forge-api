using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Quality.Coa;

/// <summary>Voids an issued certificate (it stays on record, printed VOIDED); a corrected one is generated anew.</summary>
public record VoidCertificateOfAnalysisCommand(int Id) : IRequest<CertificateOfAnalysisResponseModel>;

public class VoidCertificateOfAnalysisHandler(AppDbContext db)
    : IRequestHandler<VoidCertificateOfAnalysisCommand, CertificateOfAnalysisResponseModel>
{
    public async Task<CertificateOfAnalysisResponseModel> Handle(
        VoidCertificateOfAnalysisCommand request, CancellationToken cancellationToken)
    {
        var certificate = await db.CertificatesOfAnalysis.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Certificate of analysis {request.Id} not found.");
        if (certificate.Status == CertificateOfAnalysisStatus.Voided)
            throw new InvalidOperationException($"Certificate {certificate.CertificateNumber} is already voided.");

        certificate.Status = CertificateOfAnalysisStatus.Voided;
        var description = $"Certificate of analysis {certificate.CertificateNumber} voided";
        if (certificate.ShipmentId is int shipmentId)
            db.LogActivityAt("coa-voided", description, ("Lot", certificate.LotRecordId), ("Shipment", shipmentId));
        else
            db.LogActivityAt("coa-voided", description, ("Lot", certificate.LotRecordId));
        await db.SaveChangesAsync(cancellationToken);

        var list = await CertificateOfAnalysisMapping.ListAsync(
            db, db.CertificatesOfAnalysis.Where(c => c.Id == certificate.Id), cancellationToken);
        return list[0];
    }
}
