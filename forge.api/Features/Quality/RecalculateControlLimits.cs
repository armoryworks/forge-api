using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record RecalculateControlLimitsCommand(int CharacteristicId, int? FromSubgroup, int? ToSubgroup)
    : IRequest<SpcControlLimitModel>;

public class RecalculateControlLimitsHandler(AppDbContext db, ISpcService spcService)
    : IRequestHandler<RecalculateControlLimitsCommand, SpcControlLimitModel>
{
    private const int RuleWindow = 10;

    public async Task<SpcControlLimitModel> Handle(
        RecalculateControlLimitsCommand request, CancellationToken cancellationToken)
    {
        var limits = await spcService.CalculateControlLimitsAsync(
            request.CharacteristicId, request.FromSubgroup, request.ToSubgroup, cancellationToken);

        await ReflagStoredSubgroupsAsync(request.CharacteristicId, limits, cancellationToken);

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

    private async Task ReflagStoredSubgroupsAsync(int characteristicId, SpcControlLimit limits, CancellationToken ct)
    {
        var characteristic = await db.SpcCharacteristics.AsNoTracking()
            .FirstAsync(c => c.Id == characteristicId, ct);
        var history = await db.SpcMeasurements
            .Where(m => m.CharacteristicId == characteristicId)
            .OrderBy(m => m.SubgroupNumber)
            .ToListAsync(ct);

        for (var i = 0; i < history.Count; i++)
        {
            var start = Math.Max(0, i - (RuleWindow - 1));
            var rule = spcService.EvaluateControlRules(characteristic, limits, history.GetRange(start, i - start + 1))
                .Select(e => e.RuleName)
                .FirstOrDefault();
            history[i].IsOutOfControl = rule is not null;
            history[i].OocRuleViolated = rule;
        }

        await db.SaveChangesAsync(ct);
    }
}
