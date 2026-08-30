using MediatR;

using Forge.Api.Services;
using Forge.Core.Models;

namespace Forge.Api.Features.Admin.Telemetry;

/// <summary>
/// The agreement to show before the switch is thrown, including the verbatim sample
/// payload. Static — it ships with the build, so the terms someone accepts are the
/// terms that were on screen.
/// </summary>
public record GetTelemetryAgreementQuery : IRequest<TelemetryAgreementResponseModel>;

public class GetTelemetryAgreementHandler : IRequestHandler<GetTelemetryAgreementQuery, TelemetryAgreementResponseModel>
{
    public Task<TelemetryAgreementResponseModel> Handle(GetTelemetryAgreementQuery request, CancellationToken ct)
        => Task.FromResult(TelemetryAgreement.Current);
}
