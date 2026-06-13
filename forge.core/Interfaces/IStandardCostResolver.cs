using Forge.Core.Models.Accounting;

namespace Forge.Core.Interfaces;

/// <summary>
/// Resolves a part's standard unit cost decomposed into material / labor / overhead — the backbone of the
/// standard-cost variance decomposition. The elements always sum to the part's blended standard unit cost.
/// </summary>
public interface IStandardCostResolver
{
    Task<StandardCostElements> ResolveAsync(int partId, CancellationToken ct = default);
}
