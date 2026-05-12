using Microsoft.Extensions.Logging;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class MockAbcClassificationService(ILogger<MockAbcClassificationService> logger) : IAbcClassificationService
{
    public Task<AbcClassificationRun> RunClassificationAsync(AbcClassificationParametersModel parameters, CancellationToken ct)
    {
        logger.LogInformation("[MockAbcClassification] RunClassification with A={AThreshold}%, B={BThreshold}%, Lookback={Months}mo",
            parameters.ClassAThresholdPercent, parameters.ClassBThresholdPercent, parameters.LookbackMonths);
        var run = new AbcClassificationRun
        {
            Id = 1,
            RunDate = DateTimeOffset.UtcNow,
            TotalParts = 0,
            ClassACount = 0,
            ClassBCount = 0,
            ClassCCount = 0,
            ClassAThresholdPercent = parameters.ClassAThresholdPercent,
            ClassBThresholdPercent = parameters.ClassBThresholdPercent,
            TotalAnnualUsageValue = 0,
            LookbackMonths = parameters.LookbackMonths,
        };
        return Task.FromResult(run);
    }

    public Task<AbcClassificationRun?> GetLatestRunAsync(CancellationToken ct)
    {
        logger.LogInformation("[MockAbcClassification] GetLatestRun");
        return Task.FromResult<AbcClassificationRun?>(null);
    }

    public Task<IReadOnlyList<AbcClassification>> GetClassificationsByRunAsync(int runId, CancellationToken ct)
    {
        logger.LogInformation("[MockAbcClassification] GetClassificationsByRun {RunId}", runId);
        return Task.FromResult<IReadOnlyList<AbcClassification>>([]);
    }

    public Task<AbcClassificationSummaryResponseModel> GetSummaryAsync(CancellationToken ct)
    {
        logger.LogInformation("[MockAbcClassification] GetSummary");
        return Task.FromResult(new AbcClassificationSummaryResponseModel
        {
            LastRunDate = null,
            ClassACount = 0,
            ClassBCount = 0,
            ClassCCount = 0,
            ClassAValuePercent = 0,
            ClassBValuePercent = 0,
            ClassCValuePercent = 0,
        });
    }

    public Task ApplyToPartsAsync(int runId, CancellationToken ct)
    {
        logger.LogInformation("[MockAbcClassification] ApplyToParts RunId={RunId}", runId);
        return Task.CompletedTask;
    }
}
