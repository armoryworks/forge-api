using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IMrpService
{
    Task<MrpRunResponseModel> ExecuteRunAsync(MrpRunOptions options, CancellationToken cancellationToken = default);
    Task<MrpPartPlanResponseModel> GetPartPlanAsync(int mrpRunId, int partId, CancellationToken cancellationToken = default);
    Task<List<MrpPeggingResponseModel>> GetPeggingAsync(int mrpRunId, int partId, CancellationToken cancellationToken = default);
}
