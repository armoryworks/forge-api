using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IAbcClassificationService
{
    Task<AbcClassificationRun> RunClassificationAsync(AbcClassificationParametersModel parameters, CancellationToken ct);
    Task<AbcClassificationRun?> GetLatestRunAsync(CancellationToken ct);
    Task<IReadOnlyList<AbcClassification>> GetClassificationsByRunAsync(int runId, CancellationToken ct);
    Task<AbcClassificationSummaryResponseModel> GetSummaryAsync(CancellationToken ct);
    Task ApplyToPartsAsync(int runId, CancellationToken ct);
}
