using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality.Coa;

public record GenerateCertificateOfAnalysisCommand(GenerateCertificateOfAnalysisRequestModel Data)
    : IRequest<CertificateOfAnalysisResponseModel>;

public class GenerateCertificateOfAnalysisCommandValidator : AbstractValidator<GenerateCertificateOfAnalysisCommand>
{
    public GenerateCertificateOfAnalysisCommandValidator()
    {
        RuleFor(x => x.Data.LotId).GreaterThan(0);
        RuleFor(x => x.Data.Notes).MaximumLength(2000);
        RuleFor(x => x.Data.Language).MaximumLength(10);
    }
}

public class GenerateCertificateOfAnalysisHandler(
    AppDbContext db,
    CertificateOfAnalysisIssuer issuer,
    IHttpContextAccessor httpContext) : IRequestHandler<GenerateCertificateOfAnalysisCommand, CertificateOfAnalysisResponseModel>
{
    public async Task<CertificateOfAnalysisResponseModel> Handle(
        GenerateCertificateOfAnalysisCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        var userId = int.TryParse(httpContext.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : (int?)null;
        var certificate = await issuer.IssueAsync(
            data.LotId, data.CustomerId, data.ShipmentId, data.Language, data.Notes, userId, cancellationToken);
        var list = await CertificateOfAnalysisMapping.ListAsync(
            db, db.CertificatesOfAnalysis.Where(c => c.Id == certificate.Id), cancellationToken);
        return list[0];
    }
}
