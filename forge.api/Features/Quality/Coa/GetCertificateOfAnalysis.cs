using MediatR;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality.Coa;

public record GetCertificateOfAnalysisQuery(int Id) : IRequest<CertificateOfAnalysisResponseModel>;

public class GetCertificateOfAnalysisHandler(AppDbContext db)
    : IRequestHandler<GetCertificateOfAnalysisQuery, CertificateOfAnalysisResponseModel>
{
    public async Task<CertificateOfAnalysisResponseModel> Handle(
        GetCertificateOfAnalysisQuery request, CancellationToken cancellationToken)
    {
        var list = await CertificateOfAnalysisMapping.ListAsync(
            db, db.CertificatesOfAnalysis.Where(c => c.Id == request.Id), cancellationToken);
        return list.FirstOrDefault()
            ?? throw new KeyNotFoundException($"Certificate of analysis {request.Id} not found.");
    }
}
