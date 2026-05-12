using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Quality;

public record RecalculateControlLimitsCommand(int CharacteristicId, int? FromSubgroup, int? ToSubgroup)
    : IRequest<SpcControlLimitModel>;

public class RecalculateControlLimitsHandler(ISpcService spcService)
    : IRequestHandler<RecalculateControlLimitsCommand, SpcControlLimitModel>
{
    public async Task<SpcControlLimitModel> Handle(
        RecalculateControlLimitsCommand request, CancellationToken cancellationToken)
    {
        var limits = await spcService.CalculateControlLimitsAsync(
            request.CharacteristicId, request.FromSubgroup, request.ToSubgroup, cancellationToken);

        return new SpcControlLimitModel
        {
            XBarUcl = limits.XBarUcl,
            XBarLcl = limits.XBarLcl,
            XBarCenterLine = limits.XBarCenterLine,
            RangeUcl = limits.RangeUcl,
            RangeLcl = limits.RangeLcl,
            RangeCenterLine = limits.RangeCenterLine,
            Cp = limits.Cp,
            Cpk = limits.Cpk,
            Pp = limits.Pp,
            Ppk = limits.Ppk,
            ProcessSigma = limits.ProcessSigma,
            SampleCount = limits.SampleCount,
            IsActive = limits.IsActive,
        };
    }
}
