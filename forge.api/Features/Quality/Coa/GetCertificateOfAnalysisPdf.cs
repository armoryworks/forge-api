using MediatR;

namespace Forge.Api.Features.Quality.Coa;

/// <summary>The certificate's PDF rendered from its frozen results, in the requested language (else the language it was issued in).</summary>
public record GetCertificateOfAnalysisPdfQuery(int Id, string? Language) : IRequest<byte[]>;

public class GetCertificateOfAnalysisPdfHandler(CertificateOfAnalysisIssuer issuer)
    : IRequestHandler<GetCertificateOfAnalysisPdfQuery, byte[]>
{
    public Task<byte[]> Handle(GetCertificateOfAnalysisPdfQuery request, CancellationToken cancellationToken)
        => issuer.RenderPdfAsync(request.Id, request.Language, cancellationToken);
}
