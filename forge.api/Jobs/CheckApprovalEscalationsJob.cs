using Forge.Core.Interfaces;

namespace Forge.Api.Jobs;

public class CheckApprovalEscalationsJob(IApprovalService approvalService)
{
    public async Task ExecuteAsync(CancellationToken ct)
    {
        await approvalService.CheckEscalationsAsync(ct);
    }
}
