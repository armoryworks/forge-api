using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface ISpcService
{
    Task<SpcControlLimit> CalculateControlLimitsAsync(int characteristicId, int? fromSubgroup, int? toSubgroup, CancellationToken ct);

    decimal CalculateCp(decimal usl, decimal lsl, decimal sigma);
    decimal CalculateCpk(decimal usl, decimal lsl, decimal mean, decimal sigma);
    decimal CalculatePp(decimal usl, decimal lsl, decimal overallSigma);
    decimal CalculatePpk(decimal usl, decimal lsl, decimal mean, decimal overallSigma);

    IReadOnlyList<SpcOocEvent> EvaluateControlRules(SpcCharacteristic characteristic, SpcControlLimit limits, IReadOnlyList<SpcMeasurement> recentSubgroups);

    Task<SpcChartDataModel> GetXBarRChartDataAsync(int characteristicId, int? lastNSubgroups, CancellationToken ct);

    SpcConstants GetConstants(int sampleSize);
}

public record SpcConstants(decimal A2, decimal D3, decimal D4, decimal d2, decimal A3, decimal B3, decimal B4, decimal c4);
